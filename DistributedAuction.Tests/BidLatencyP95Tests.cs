using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Repositories;
using DistributedAuction.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace DistributedAuction.Tests;
[TestFixture]
[NonParallelizable] // evita paralelizar esta fixture com outras
public class BidLatencyP95NUnitTests
{
    private SqliteConnection _conn = null!;
    private DbContextOptions<AuctionDbContext> _opts = null!;

    [SetUp]
    public async Task SetUp()
    {
        // ÚNICA conexão aberta => todos os DbContexts compartilham o MESMO banco in-memory
        _conn = new SqliteConnection("Data Source=:memory:");
        await _conn.OpenAsync();

        _opts = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(_conn)
            .EnableSensitiveDataLogging()
            .Options;

        // Cria schema uma única vez
        await using var db = new AuctionDbContext(_opts);
        await db.Database.MigrateAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await _conn.CloseAsync();
        await _conn.DisposeAsync();
    }

    [Test]
    public async Task P95_should_be_below_threshold()
    {
        // -------- Config --------
        int totalBids = GetEnvInt("P95_TOTAL_BIDS", 2000);     // 1–5k é razoável localmente
        int degreeOfParallelism = GetEnvInt("P95_DOP", 32);    // ajuste conforme CPU
        double thresholdMs = GetEnvDouble("P95_THRESHOLD_MS", 200.0);

        // -------- Cria auction (via um service dedicado) --------
        var svcForCreate = NewService(); // cada service tem seu próprio DbContext
        var auctionReq = new CreateAuctionRequest
        {
            VehicleId = Guid.NewGuid(),
            Region = "US-East",
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow.AddMinutes(2),
            State = AuctionState.Created
        };
        var auction = await svcForCreate.CreateAuctionAsync(auctionReq);

        // -------- Warm-up (amortece JIT/alocações) --------
        for (int i = 1; i <= 50; i++)
        {
            await svcForCreate.PlaceBidAsync(auction.Id, new BidRequest
            {
                OriginRegion = "US-East",
                TargetRegion = "US-East",
                UserId = "warmup",
                Amount = i,
                DeduplicationKey = Guid.NewGuid().ToString()
            });
        }

        // -------- Execução concorrente --------
        var latencies = new ConcurrentBag<double>();
        var errors = new ConcurrentBag<Exception>();
        long amountCounter = 50;
        var throttler = new SemaphoreSlim(degreeOfParallelism);

        var tasks = Enumerable.Range(0, totalBids).Select(async _ =>
        {
            await throttler.WaitAsync();
            try
            {
                // Cada tarefa usa UM service novo (com seu próprio DbContext)
                var svc = NewService();

                var amount = Interlocked.Increment(ref amountCounter);
                var sw = Stopwatch.StartNew();

                await svc.PlaceBidAsync(auction.Id, new BidRequest
                {
                    OriginRegion = "US-East",
                    TargetRegion = "US-East",
                    UserId = "load",
                    Amount = amount,
                    DeduplicationKey = Guid.NewGuid().ToString()
                });

                sw.Stop();
                latencies.Add(sw.Elapsed.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
            finally
            {
                throttler.Release();
            }
        }).ToArray();

        await Task.WhenAll(tasks);

        // -------- Asserts e métrica --------
        if (!errors.IsEmpty)
        {
            var msgs = string.Join(Environment.NewLine, errors.Take(5).Select(e => "- " + e.GetType().Name + ": " + e.Message));
            Assert.Fail($"Ocorreram {errors.Count} exceções durante o teste de carga (mostrando até 5):{Environment.NewLine}{msgs}");
        }

        var p95 = Percentile([.. latencies], 95);
        TestContext.Out.WriteLine($"Bids={totalBids}, DoP={degreeOfParallelism}, p95={p95:F1} ms (threshold={thresholdMs:F1} ms)");

        Assert.That(p95, Is.LessThanOrEqualTo(thresholdMs),
            $"p95 {p95:F1} ms > threshold {thresholdMs:F1} ms. " +
            $"Considere ajustar P95_TOTAL_BIDS/P95_DOP ou rodar em Release sem debugger.");
    }

    // ---------- Helpers ----------

    /// <summary>Cada chamada cria um IAuctionService completo com DbContext PRÓPRIO sobre a mesma conexão.</summary>
    private IAuctionService NewService()
    {
        var db = new AuctionDbContext(_opts);

        IAuctionRepository auctionRepo = new AuctionRepository(db);
        IBidRepository bidRepo = new BidRepository(db);
        IAuctionSequenceService sequenceSvc = new AuctionSequenceService(db);
        IBidOrderingService orderingSvc = new BidOrderingService(sequenceSvc, db);

        IRegionCoordinator region = new RegionCoordinator(); // saudável por padrão
        IConflictResolver resolver = new ConflictResolver();

        return new AuctionService(
            auctionRepo,
            bidRepo,
            sequenceSvc,
            orderingSvc,
            region,
            resolver,
            db
        );
    }

    private static double Percentile(double[] values, int percentile)
    {
        if (values == null || values.Length == 0) return 0;
        Array.Sort(values);
        var rank = (int)Math.Ceiling(percentile / 100.0 * values.Length) - 1;
        rank = Math.Clamp(rank, 0, values.Length - 1);
        return values[rank];
    }

    private static int GetEnvInt(string name, int def)
        => int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : def;

    private static double GetEnvDouble(string name, double def)
        => double.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : def;
}