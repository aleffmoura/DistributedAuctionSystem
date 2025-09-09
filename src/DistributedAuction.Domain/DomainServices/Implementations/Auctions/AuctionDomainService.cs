using DistributedAuction.Domain.DomainServices.Interfaces.Auctions;
using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;

namespace DistributedAuction.Domain.DomainServices.Implementations.Auctions;

public class AuctionDomainService : IAuctionDomainService
{
    public Auction Configure(Auction auction)
    {
        if (auction.StartTime == default)
            auction.UpdateStart(DateTime.UtcNow);

        if (auction.EndTime <= auction.StartTime)
            auction.UpdateEnd(auction.StartTime.AddMinutes(5));

        auction.Start();

        return auction;
    }

    public Auction Create(CreateAuctionRequest auctionRequest)
    {
        var auction = Auction.Create(
            auctionRequest.VehicleId,
            auctionRequest.EndTime,
            auctionRequest.StartTime,
            auctionRequest.Region
        );

        if(auction.IsValid())
            return auction;

        throw new Exception("invalid object");
    }

    public Auction UpdateVersion(Auction auction)
    {
        throw new NotImplementedException();
    }

    public async ValueTask<Auction> UpdateIfWinner(Auction auction, Bid bid, IAuctionRepository auctionRepository)
    {
        if (bid != null && bid.Amount > auction.HighestAmount)
        {
            auction.UpdateWinner(bid.Id, bid.Amount);
            await auctionRepository.UpdateAsync(auction);
        }

        return auction;
    }
}
