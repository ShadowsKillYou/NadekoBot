﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.Commands;
using NadekoBot.Services;
using NadekoBot.Attributes;
using NadekoBot.Services.Database;
using System.Collections.Concurrent;
using NadekoBot.Services.Database.Models;
using Discord;
using NadekoBot.Extensions;

namespace NadekoBot.Modules.CustomReactions
{    
    [NadekoModule("CustomReactions",".")]
    public class CustomReactions : DiscordModule
    {
        public static ConcurrentHashSet<CustomReaction> GlobalReactions { get; } = new ConcurrentHashSet<CustomReaction>();
        public static ConcurrentDictionary<ulong, ConcurrentHashSet<CustomReaction>> GuildReactions { get; } = new ConcurrentDictionary<ulong, ConcurrentHashSet<CustomReaction>>();
        static CustomReactions()
        {
            using (var uow = DbHandler.UnitOfWork())
            {
                var items = uow.CustomReactions.GetAll();
                GuildReactions = new ConcurrentDictionary<ulong, ConcurrentHashSet<CustomReaction>>(items.Where(g => g.GuildId != null && g.GuildId != 0).GroupBy(k => k.GuildId.Value).ToDictionary(g => g.Key, g => new ConcurrentHashSet<CustomReaction>(g)));
                GlobalReactions = new ConcurrentHashSet<CustomReaction>(items.Where(g => g.GuildId == null || g.GuildId == 0));
            }
            NadekoBot.Client.MessageReceived += (imsg) =>
            {
                var umsg = imsg as IUserMessage;
                if (umsg == null || imsg.Author.IsBot)
                    return Task.CompletedTask;

                var channel = umsg.Channel as ITextChannel;
                if (channel == null)
                    return Task.CompletedTask;

                var t = Task.Run(async () =>
                {
                    var content = umsg.Content.Trim().ToLowerInvariant();
                    ConcurrentHashSet<CustomReaction> reactions;
                    GuildReactions.TryGetValue(channel.Guild.Id, out reactions);
                    if (reactions != null && reactions.Any())
                    {
                        var reaction = reactions.Where(cr => {
                            var hasTarget = cr.Response.ToLowerInvariant().Contains("%target%");
                            var trigger = cr.TriggerWithContext(umsg).Trim().ToLowerInvariant();
                            return ((hasTarget && content.StartsWith(trigger + " ")) || content == trigger);
                        }).Shuffle().FirstOrDefault();
                        if (reaction != null)
                        {
                            try { await channel.SendMessageAsync(reaction.ResponseWithContext(umsg)).ConfigureAwait(false); } catch { }
                            return;
                        }
                    }
                    var greaction = GlobalReactions.Where(cr =>
                    {
                        var hasTarget = cr.Response.ToLowerInvariant().Contains("%target%");
                        var trigger = cr.TriggerWithContext(umsg).Trim().ToLowerInvariant();
                        return ((hasTarget && content.StartsWith(trigger + " ")) || content == trigger);
                    }).Shuffle().FirstOrDefault();

                    if (greaction != null)
                    {
                        try { await channel.SendMessageAsync(greaction.ResponseWithContext(umsg)).ConfigureAwait(false); } catch { }
                        return;
                    }
                });
                return Task.CompletedTask;
            };
        }

        public CustomReactions() : base()
        {
        }

        [NadekoCommand, Usage, Description, Aliases]
        public async Task AddCustReact(string key, [Remainder] string message)
        {
            var channel = Context.Channel as ITextChannel;
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(key))
                return;

            key = key.ToLowerInvariant();

            if ((channel == null && !NadekoBot.Credentials.IsOwner(Context.User)) || (channel != null && !((IGuildUser)Context.User).GuildPermissions.Administrator))
            {
                try { await Context.Channel.SendMessageAsync("Insufficient permissions. Requires Bot ownership for global custom reactions, and Administrator for guild custom reactions."); } catch { }
                return;
            }

            var cr = new CustomReaction()
            {
                GuildId = channel?.Guild.Id,
                IsRegex = false,
                Trigger = key,
                Response = message,
            };

            using (var uow = DbHandler.UnitOfWork())
            {
                uow.CustomReactions.Add(cr);

                await uow.CompleteAsync().ConfigureAwait(false);
            }

            if (channel == null)
            {
                GlobalReactions.Add(cr);
            }
            else
            {
                var reactions = GuildReactions.GetOrAdd(channel.Guild.Id, new ConcurrentHashSet<CustomReaction>());
                reactions.Add(cr);
            }

            await Context.Channel.SendMessageAsync($"`Added new custom reaction {cr.Id}:`\n\t`Trigger:` {key}\n\t`Response:` {message}").ConfigureAwait(false);
        }

        [NadekoCommand, Usage, Description, Aliases]
        public async Task ListCustReact(int page = 1)
        {
            var channel = Context.Channel as ITextChannel;

            if (page < 1 || page > 1000)
                return;
            ConcurrentHashSet<CustomReaction> customReactions;
            if (channel == null)
                customReactions = GlobalReactions;
            else
                customReactions = GuildReactions.GetOrAdd(channel.Guild.Id, new ConcurrentHashSet<CustomReaction>());

            if (customReactions == null || !customReactions.Any())
                await Context.Channel.SendMessageAsync("`No custom reactions found`").ConfigureAwait(false);
            else
                await Context.Channel.SendMessageAsync($"`Page {page} of custom reactions:`\n" + string.Join("\n", customReactions.OrderBy(cr => cr.Trigger).Skip((page - 1) * 15).Take(15).Select(cr => $"`#{cr.Id}`  `Trigger:` {cr.Trigger}")))
                             .ConfigureAwait(false);
        }

        [NadekoCommand, Usage, Description, Aliases]
        public async Task ShowCustReact(int id)
        {
            var channel = Context.Channel as ITextChannel;

            ConcurrentHashSet<CustomReaction> customReactions;
            if (channel == null)
                customReactions = GlobalReactions;
            else
                customReactions = GuildReactions.GetOrAdd(channel.Guild.Id, new ConcurrentHashSet<CustomReaction>());

            var found = customReactions.FirstOrDefault(cr => cr.Id == id);

            if (found == null)
                await Context.Channel.SendMessageAsync("`No custom reaction found with that id.`")
                             .ConfigureAwait(false);
            else
            {
                await Context.Channel.SendMessageAsync($"`Custom reaction #{id}`\n`Trigger:` {found.Trigger}\n`Response:` {found.Response}")
                             .ConfigureAwait(false);
            }
        }

        [NadekoCommand, Usage, Description, Aliases]
        public async Task DelCustReact(int id)
        {
            var channel = Context.Channel as ITextChannel;

            if ((channel == null && !NadekoBot.Credentials.IsOwner(Context.User)) || (channel != null && !((IGuildUser)Context.User).GuildPermissions.Administrator))
            {
                try { await Context.Channel.SendMessageAsync("Insufficient permissions. Requires Bot ownership for global custom reactions, and Administrator for guild custom reactions."); } catch { }
                return;
            }

            var success = false;
            CustomReaction toDelete;
            using (var uow = DbHandler.UnitOfWork())
            {
                toDelete = uow.CustomReactions.Get(id);
                if (toDelete == null) //not found
                    return;

                if ((toDelete.GuildId == null || toDelete.GuildId == 0) && channel == null)
                {
                    uow.CustomReactions.Remove(toDelete);
                    GlobalReactions.RemoveWhere(cr => cr.Id == toDelete.Id);
                    success = true;
                }
                else if ((toDelete.GuildId != null && toDelete.GuildId != 0) && channel?.Guild.Id == toDelete.GuildId)
                {
                    uow.CustomReactions.Remove(toDelete);
                    GuildReactions.GetOrAdd(channel.Guild.Id, new ConcurrentHashSet<CustomReaction>()).RemoveWhere(cr => cr.Id == toDelete.Id);
                    success = true;
                }
                if(success)
                    await uow.CompleteAsync().ConfigureAwait(false);
            }

            if (success)
                await Context.Channel.SendMessageAsync("**Successfully deleted custom reaction** " + toDelete.ToString()).ConfigureAwait(false);
            else
                await Context.Channel.SendMessageAsync("`Failed to find that custom reaction.`").ConfigureAwait(false);
        }
    }
}
