using DistributedAuction.Domain.Entities;
using DistributedAuction.Domain.Interfaces;
using DistributedAuction.Domain.Models;

namespace DistributedAuction.Domain.DomainServices.Interfaces.Auctions;

public interface IAuctionDomainService
{
    Auction Create(CreateAuctionRequest createAuctionRequest);
    Auction Configure(Auction configureAuction);
    Auction UpdateVersion(Auction auction);
    ValueTask<Auction> UpdateIfWinner(Auction auction, Bid bid, IAuctionRepository auctionRepository);
}
