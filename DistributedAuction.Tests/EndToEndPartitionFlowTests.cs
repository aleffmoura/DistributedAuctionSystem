using DistributedAuction.Application.Services;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Enums;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;
using DistributedAuction.Infrastructure.Persistence;
using DistributedAuction.Infrastructure.Repositories;
using DistributedAuction.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DistributedAuction.Tests;

public class EndToEndPartitionFlowTests
{
    private AuctionDbContext _db = null!;
    private IAuctionRepository _auctionRepo = null!;
    private IBidRepository _bidRepo = null!;
    private IAuctionSequenceService _sequenceSvc = null!;
    private IBidOrderingService _orderingSvc = null!;

    private IRegionCoordinator _region = null!;
    private IConflictResolver _resolver = null!;
    private IAuctionService _svc = null!;

    private SqliteConnection _conn = null!;

    [SetUp]
    public async Task SetUp()
    {
        // In-memory SQLite backed by a single open connection so the schema lives across contexts
        _conn = new SqliteConnection("DataSource=:memory:");
        await _conn.OpenAsync();

        var opts = new DbContextOptionsBuilder<AuctionDbContext>()
            .UseSqlite(_conn)
            .EnableSensitiveDataLogging()
            .Options;

        _db = new AuctionDbContext(opts);
        await _db.Database.MigrateAsync();
        // Repositories & services (use os concretos do seu projeto)
        _auctionRepo = new AuctionRepository(_db);
        _bidRepo = new BidRepository(_db);
        _sequenceSvc = new AuctionSequenceService(_db);
        _orderingSvc = new BidOrderingService(_sequenceSvc, _db);

        // RegionCoordinator e ConflictResolver (implementações reais do seu projeto)
        _region = new RegionCoordinator();      // se o seu ctor exigir regiões, ajuste aqui
        _resolver = new ConflictResolver();

        _svc = new AuctionService(
            _auctionRepo,
            _bidRepo,
            _sequenceSvc,
            _orderingSvc,
            _region,
            _resolver,
            _db
        );
    }

    [TearDown]
    public async Task TearDown()
    {
        await _conn.CloseAsync();
        await _conn.DisposeAsync();
        await _db.DisposeAsync();
    }

    [Test]
    public async Task Auction_partition_end_to_end_should_select_correct_winner_and_lose_no_bids()
    {
        // arrange: cria auction em US-East com window curta
        var auctionReq = new CreateAuctionRequest
        {
            VehicleId = Guid.NewGuid(),
            Region = "US-East",
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow.AddSeconds(10),
            State = AuctionState.Created
        };
        var auction = await _svc.CreateAuctionAsync(auctionReq);

        // Simula partição US <-> EU
        // (ajuste os métodos conforme sua implementação de RegionCoordinator)
        if (_region is RegionCoordinator rc1)
            rc1.SimulatePartition("US-East", "EU-West");

        // EU-West tenta dar lance no auction US-East (maior) DURANTE a partição -> Pending
        var euRequest = new BidRequest
        {
            OriginRegion = "EU-West",
            TargetRegion = "US-East",
            UserId = "eu-user",
            Amount = 120m,
            DeduplicationKey = Guid.NewGuid().ToString()
        };
        var euResult = await _svc.PlaceBidAsync(auction.Id, euRequest);
        Assert.That(euResult.Status, Is.EqualTo(BidStatus.PendingPartition), "EU bid should be queued as Pending during partition");

        // US-East dá um lance menor localmente -> Accepted (forte)
        var usRequest = new BidRequest
        {
            OriginRegion = "US-East",
            TargetRegion = "US-East",
            UserId = "us-user",
            Amount = 100m,
            DeduplicationKey = Guid.NewGuid().ToString()
        };
        var usResult = await _svc.PlaceBidAsync(auction.Id, usRequest);
        Assert.That(usResult.Status, Is.EqualTo(BidStatus.Accepted), "US bid should be accepted locally");

        // Força o término do leilão (EndTime no passado)
        auction.UpdateEnd(DateTime.UtcNow.AddSeconds(-1));

        await _auctionRepo.UpdateAsync(auction);

        // Cura a partição
        if (_region is RegionCoordinator rc2)
            rc2.HealPartition("US-East", "EU-West");

        // act: reconcilia (aplica o pending cross-region se on-time)
        var reconcile = await _svc.ReconcileAuctionAsync(auction.Id);
        Assert.That(reconcile.Success, Is.True, reconcile.Details);

        // assert: sem perda de lances e vencedor correto (EU = 120)
        var final = await _auctionRepo.GetAsync(auction.Id);
        Assert.That(final, Is.Not.Null);
        Assert.That(final!.HighestAmount, Is.EqualTo(120m), "Winner should be the higher on-time EU bid after reconciliation");
        Assert.That(final.State, Is.EqualTo(AuctionState.Reconciled));

        var bids = await _db.Bids.Where(b => b.AuctionId == auction.Id).ToListAsync();
        Assert.That(bids.Count, Is.GreaterThanOrEqualTo(2), "Both US and EU bids should exist after reconcile");
        Assert.That(bids.Any(b => b.Amount == 100m && b.UserId == "us-user"), Is.True);
        Assert.That(bids.Any(b => b.Amount == 120m && b.UserId == "eu-user"), Is.True);
    }

    [Test]
    public async Task Cross_region_healthy_network_executes_in_owner_region()
    {
        // arrange: auction em US
        var auctionReq = new CreateAuctionRequest
        {
            VehicleId = Guid.NewGuid(),
            Region = "US-East",
            StartTime = DateTime.UtcNow.AddSeconds(-5),
            EndTime = DateTime.UtcNow.AddSeconds(30),
            State = AuctionState.Created
        };
        var auction = await _svc.CreateAuctionAsync(auctionReq);

        // Garante rede saudável (sem partição)
        // (Se seu RegionCoordinator inicia saudável por padrão, não precisa de nada aqui)

        // act: EU dá lance em auction US com rede saudável -> deve ser Accepted (via ExecuteInRegionAsync)
        var req = new BidRequest
        {
            OriginRegion = "EU-West",
            TargetRegion = "US-East",
            UserId = "eu-user",
            Amount = 50m,
            DeduplicationKey = Guid.NewGuid().ToString()
        };
        var res = await _svc.PlaceBidAsync(auction.Id, req);

        // assert
        Assert.That(res.Status, Is.EqualTo(BidStatus.Accepted));
        var loaded = await _auctionRepo.GetAsync(auction.Id);
        Assert.That(loaded!.HighestAmount, Is.EqualTo(50m));
    }
}