﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NadekoBot.Attributes;
using NadekoBot.Services;
using NadekoBot.Services.Database;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NadekoBot.Modules.Permissions
{
    public partial class Permissions
    {
        [Group]
        public class FilterCommands : ModuleBase
        {
            public static ConcurrentHashSet<ulong> InviteFilteringChannels { get; }
            public static ConcurrentHashSet<ulong> InviteFilteringServers { get; }

            //serverid, filteredwords
            private static ConcurrentDictionary<ulong, ConcurrentHashSet<string>> ServerFilteredWords { get; }

            public static ConcurrentHashSet<ulong> WordFilteringChannels { get; }
            public static ConcurrentHashSet<ulong> WordFilteringServers { get; }

            public static ConcurrentHashSet<string> FilteredWordsForChannel(ulong channelId, ulong guildId)
            {
                ConcurrentHashSet<string> words = new ConcurrentHashSet<string>();
                if(WordFilteringChannels.Contains(channelId))
                    ServerFilteredWords.TryGetValue(guildId, out words);
                return words;
            }

            public static ConcurrentHashSet<string> FilteredWordsForServer(ulong guildId)
            {
                var words = new ConcurrentHashSet<string>();
                if(WordFilteringServers.Contains(guildId))
                    ServerFilteredWords.TryGetValue(guildId, out words);
                return words;
            }

            static FilterCommands()
            {
                using (var uow = DbHandler.UnitOfWork())
                {
                    var guildConfigs = NadekoBot.AllGuildConfigs;

                    InviteFilteringServers = new ConcurrentHashSet<ulong>(guildConfigs.Where(gc => gc.FilterInvites).Select(gc => gc.GuildId));
                    InviteFilteringChannels = new ConcurrentHashSet<ulong>(guildConfigs.SelectMany(gc => gc.FilterInvitesChannelIds.Select(fci => fci.ChannelId)));

                    var dict = guildConfigs.ToDictionary(gc => gc.GuildId, gc => new ConcurrentHashSet<string>(gc.FilteredWords.Select(fw => fw.Word)));

                    ServerFilteredWords = new ConcurrentDictionary<ulong, ConcurrentHashSet<string>>(dict);

                    var serverFiltering = guildConfigs.Where(gc => gc.FilterWords);
                    WordFilteringServers = new ConcurrentHashSet<ulong>(serverFiltering.Select(gc => gc.GuildId));

                    WordFilteringChannels = new ConcurrentHashSet<ulong>(guildConfigs.SelectMany(gc => gc.FilterWordsChannelIds.Select(fwci => fwci.ChannelId)));

                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task SrvrFilterInv()
            {
                var channel = (SocketTextChannel)Context.Channel;

                bool enabled;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id);
                    enabled = config.FilterInvites = !config.FilterInvites;
                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                if (enabled)
                {
                    InviteFilteringServers.Add(channel.Guild.Id);
                    await channel.SendMessageAsync("`Invite filtering enabled on this server.`").ConfigureAwait(false);
                }
                else
                {
                    InviteFilteringServers.TryRemove(channel.Guild.Id);
                    await channel.SendMessageAsync("`Invite filtering disabled on this server.`").ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task ChnlFilterInv()
            {
                var channel = (SocketTextChannel)Context.Channel;

                int removed;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id);
                    removed = config.FilterInvitesChannelIds.RemoveWhere(fc => fc.ChannelId == channel.Id);
                    if (removed == 0)
                    {
                        config.FilterInvitesChannelIds.Add(new Services.Database.Models.FilterChannelId()
                        {
                            ChannelId = channel.Id
                        });
                    }
                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                if (removed == 0)
                {
                    InviteFilteringChannels.Add(channel.Id);
                    await channel.SendMessageAsync("`Invite filtering enabled on this channel.`").ConfigureAwait(false);
                }
                else
                {
                    InviteFilteringChannels.TryRemove(channel.Id);
                    await channel.SendMessageAsync("`Invite filtering disabled on this channel.`").ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task SrvrFilterWords()
            {
                var channel = (SocketTextChannel)Context.Channel;

                bool enabled;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id);
                    enabled = config.FilterWords = !config.FilterWords;
                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                if (enabled)
                {
                    WordFilteringServers.Add(channel.Guild.Id);
                    await channel.SendMessageAsync("`Word filtering enabled on this server.`").ConfigureAwait(false);
                }
                else
                {
                    WordFilteringServers.TryRemove(channel.Guild.Id);
                    await channel.SendMessageAsync("`Word filtering disabled on this server.`").ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task ChnlFilterWords()
            {
                var channel = (SocketTextChannel)Context.Channel;

                int removed;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id);
                    removed = config.FilterWordsChannelIds.RemoveWhere(fc => fc.ChannelId == channel.Id);
                    if (removed == 0)
                    {
                        config.FilterWordsChannelIds.Add(new Services.Database.Models.FilterChannelId()
                        {
                            ChannelId = channel.Id
                        });
                    }
                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                if (removed == 0)
                {
                    WordFilteringChannels.Add(channel.Id);
                    await channel.SendMessageAsync("`Word filtering enabled on this channel.`").ConfigureAwait(false);
                }
                else
                {
                    WordFilteringChannels.TryRemove(channel.Id);
                    await channel.SendMessageAsync("`Word filtering disabled on this channel.`").ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task FilterWord([Remainder] string word)
            {
                var channel = (SocketTextChannel)Context.Channel;

                word = word?.Trim().ToLowerInvariant();

                if (string.IsNullOrWhiteSpace(word))
                    return;

                int removed;
                using (var uow = DbHandler.UnitOfWork())
                {
                    var config = uow.GuildConfigs.For(channel.Guild.Id);

                    removed = config.FilteredWords.RemoveWhere(fw => fw.Word == word);

                    if (removed == 0)
                        config.FilteredWords.Add(new Services.Database.Models.FilteredWord() { Word = word });

                    await uow.CompleteAsync().ConfigureAwait(false);
                }

                var filteredWords = ServerFilteredWords.GetOrAdd(channel.Guild.Id, new ConcurrentHashSet<string>());

                if (removed == 0)
                {
                    filteredWords.Add(word);
                    await channel.SendMessageAsync($"Word `{word}` successfully added to the list of filtered words.")
                            .ConfigureAwait(false);
                }
                else
                {
                    filteredWords.TryRemove(word);
                    await channel.SendMessageAsync($"Word `{word}` removed from the list of filtered words.")
                            .ConfigureAwait(false);
                }
            }

            [NadekoCommand, Usage, Description, Aliases]
            [RequireContext(ContextType.Guild)]
            public async Task LstFilterWords()
            {
                var channel = (SocketTextChannel)Context.Channel;

                ConcurrentHashSet<string> filteredWords;
                ServerFilteredWords.TryGetValue(channel.Guild.Id, out filteredWords);

                await channel.SendMessageAsync($"`List of banned words:`\n" + string.Join(",\n", filteredWords))
                        .ConfigureAwait(false);
            }
        }
    }
}
