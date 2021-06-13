﻿namespace Kwetter.TweetService.Services
{
    using System;
    using System.Linq;
    using System.Threading.Tasks;
    using Grpc.Core;
    using Kwetter.TweetService.Extentions;
    using Kwetter.TweetService.Persistence.Context;
    using Kwetter.TweetService.Persistence.Entity;
    using Microservice.TweetGRPCService;
    using Microsoft.EntityFrameworkCore;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Tweet service class.
    /// </summary>
    public class TweetService : TweetGRPCService.TweetGRPCServiceBase
    {

        // TODO: manage status, use manager, events

        /// <summary>
        /// Default logging instance.
        /// </summary>
        private readonly ILogger<TweetService> logger;

        /// <summary>
        /// Tweet context instance.
        /// </summary>
        private readonly TweetContext context;

        /// <summary>
        /// Initializes a new instance of the <see cref="TweetService"/> class.
        /// </summary>
        /// <param name="logger">Injected logger.</param>
        /// <param name="context">Injected context.</param>
        public TweetService(ILogger<TweetService> logger, TweetContext context)
        {
            this.logger = logger;
            this.context = context;
        }

        /// <summary>
        /// Retrieves all tweets of the passed user id.
        /// </summary>
        /// <param name="request">Incoming request.</param>
        /// <param name="context">Callback context.</param>
        /// <returns><see cref="TweetResponse"/>.</returns>
        public override async Task<TweetResponse> GetTweetsByUserId(TweetRequest request, ServerCallContext context)
        {
            try
            {
                // move to manager
                return await Task.Run(() =>
                {
                    var tweets = this.context.Tweets
                        .Include(x => x.Author)
                        .Where(x => x.Author.UserId == request.UserId)
                        .Include(x => x.LikedBy)
                        .Include(x => x.Hashtags)
                        .OrderByDescending(x => x.CreatedAt)
                        .Skip(request.Page * request.Amount)
                        .Take(request.Amount)
                        .ToList();

                    var response = new TweetResponse();
                    response.Tweets.AddRange(tweets.Select(t => t.Convert()));
                    return response;
                });
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        /// <summary>
        /// Retrieves all tweets of the passed username.
        /// </summary>
        /// <param name="request">Incoming request.</param>
        /// <param name="context">Callback context.</param>
        /// <returns><see cref="TweetResponse"/>.</returns>
        public override async Task<TweetResponse> GetTweetsByUsername(TweetRequest request, ServerCallContext context)
        {
            // move to manager
            return await Task.Run(() =>
            {
                try
                {
                    var tweets = this.context.Tweets
                        .Include(x => x.Author)
                        .Where(x => x.Author.Username == request.Username)
                        .Include(x => x.LikedBy)
                        .Include(x => x.Hashtags)
                        .OrderByDescending(x => x.CreatedAt)
                        .Skip(request.Page * request.Amount)
                        .Take(request.Amount)
                        .ToList();

                    var response = new TweetResponse();
                    response.Tweets.AddRange(tweets.Select(t => t.Convert()));

                    return response;
                }
                catch (Exception ex)
                {
                    throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
                }
            });
        }

        /// <summary>
        /// Retrieves all tweets of the passed user ids. Can be used to construct the timeline of a user.
        /// </summary>
        /// <param name="request">Incoming request.</param>
        /// <param name="context">Callback context.</param>
        /// <returns><see cref="TweetResponse"/>.</returns>
        public override async Task<TweetResponse> GetTweetsByUserIds(TweetRequest request, ServerCallContext context)
        {
            // move to manager
            return await Task.Run(() =>
            {
                try
                {
                    var tweets = this.context.Tweets
                        .Include(x => x.Author)
                        .Where(x => request.UserIds
                        .Contains(x.Author.UserId))
                        .Include(x => x.LikedBy)
                        .Include(x => x.Hashtags)
                        .OrderBy(x => Guid.NewGuid())
                        .Skip(request.Page * request.Amount)
                        .Take(request.Amount)
                        .ToList();

                    var response = new TweetResponse();
                    response.Tweets.AddRange(tweets.Select(t => t.Convert()));

                    return response;
                }
                catch (Exception ex)
                {
                    throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
                }
            });
        }

        /// <summary>
        /// Creates a new tweet.
        /// </summary>
        /// <param name="request">req.</param>
        /// <param name="context">context.</param>
        /// <returns></returns>
        public override async Task<TweetResponse> PlaceTweet(PlaceTweetRequest request, ServerCallContext context)
        {
            try
            {
                var profile = this.context.ProfileReferences.FirstOrDefault(x => x.UserId == request.UserId);
                var tweet = new TweetEntity
                {
                    Author = profile,
                    Content = request.Content,
                    CreatedAt = DateTime.Now,
                };

                this.context.Tweets.Add(tweet);
                await this.context.SaveChangesAsync().ConfigureAwait(false);

                var response = new TweetResponse();
                response.Tweets.Add(tweet.Convert());

                return response;
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }

        /// <summary>
        /// Toggles a like on the tweet associated with the passed id. In case the user has not yet liked the tweet, the
        /// tweet will be liked. If the user already liked this tweet, it will be undone.
        /// </summary>
        /// <param name="request">Incoming request.</param>
        /// <param name="context">Callback context.</param>
        /// <returns><see cref="TweetResponse"/>.</returns>
        public override async Task<TweetResponse> ToggleLike(TweetOperationRequest request, ServerCallContext context)
        {
            try
            {
                var likeRecord = this.context.Likes.Include(x => x.Author).FirstOrDefault(x => x.Author.UserId == request.UserId && x.TweetId == request.TweetId);
                if (likeRecord == null)
                {
                    var profile = this.context.ProfileReferences.FirstOrDefault(x => x.UserId == request.UserId);
                    var tweet = this.context.Tweets.FirstOrDefault(x => x.Id == request.TweetId);
                    if (profile != null && tweet != null)
                    {
                        // This tweet is not yet liked by the user.
                        likeRecord = new LikeEntity
                        {
                            Author = profile,
                            Tweet = tweet,
                            TweetId = request.TweetId,
                        };

                        this.context.Likes.Add(likeRecord);
                    }
                }
                else
                {
                    // The tweet is already liked by the user.
                    this.context.Likes.Remove(likeRecord);
                    likeRecord = null;
                }

                await this.context.SaveChangesAsync().ConfigureAwait(false);
                return new TweetResponse { Status = likeRecord != null };
            }
            catch (Exception ex)
            {
                throw new RpcException(new Status(StatusCode.Unknown, ex.Message));
            }
        }
    }
}
