using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Octgn.Play;
using Octgn.Play.Actions;
using Octgn.Utils;
using System.IO;

namespace Octgn.Networking
{
    using Octgn.Core;
    using Octgn.Core.DataExtensionMethods;
    using System.Windows.Media;

    using Octgn.Core.Play;

    internal sealed class Handler
    {
        private readonly BinaryParser _binParser;

        public Handler()
        {
            _binParser = new BinaryParser(this);
        }

        public void ReceiveMessage(byte[] data)F:\Programming\OCTGN\Octgn.PlayTable\Play\CardIdentityNamer.cs
        {
            // Fix: because ReceiveMessage is called through the Dispatcher queue, we may be called
            // just after the Client has already been closed. In that case we should simply drop the message
            // (otherwise NRE may occur)
            if (K.C.Get<Client>() == null) return;

            try
            { _binParser.Parse(data); }
            finally
            { if (K.C.Get<Client>() != null) K.C.Get<Client>().Muted = 0; }
        }

        public void Binary()
        {
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Verbose, EventIds.NonGame, "Switched to binary protocol.");
            K.C.Get<Client>().Binary();
        }

        public void Error(string msg)
        {
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Error, EventIds.NonGame, "The server has returned an error: {0}", msg);
        }

        public void Start()
        {
            Program.StartGame();
        }

        public void Settings(bool twoSidedTable)
        {
            // The host is the driver for this flag and should ignore notifications,
            // otherwise there might be a loop if the server takes more time to dispatch this message
            // than the user to click again on the checkbox.
            if (!Program.IsHost)
                Program.GameSettings.UseTwoSidedTable = twoSidedTable;
        }

        public void PlayerSettings(IPlayPlayer player, bool invertedTable)
        {
            player.InvertedTable = invertedTable;
        }

        public void Reset(IPlayPlayer player)
        {
            K.C.Get<IGameEngine>().Reset();
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(player), "{0} resets the game.", player);
        }

        public void NextTurn(IPlayPlayer player)
        {
            K.C.Get<IGameEngine>().TurnNumber++;
            K.C.Get<IGameEngine>().TurnPlayer = player;
            K.C.Get<IGameEngine>().StopTurn = false;
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Turn, "Turn {0}: {1}", K.C.Get<IGameEngine>().TurnNumber, player);
        }

        public void StopTurn(IPlayPlayer player)
        {
            if (player == K.C.Get<PlayerStateMachine>().LocalPlayer)
                K.C.Get<IGameEngine>().StopTurn = false;
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(player), "{0} wants to play before end of turn.", player);
        }

        public void Chat(IPlayPlayer player, string text)
        {
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Chat | EventIds.PlayerFlag(player), "<{0}> {1}", player, text);
        }

        public void Print(IPlayPlayer player, string text)
        {
            K.C.Get<GameplayTrace>().Print(player, text);
        }

        public void Random(IPlayPlayer player, int id, int min, int max)
        {
            var req = new RandomRequest(player, id, min, max);
            K.C.Get<IGameEngine>().RandomRequests.Add(req);
            req.Answer1();
        }

        public void RandomAnswer1(IPlayPlayer player, int id, ulong value)
        {
            var req = K.C.Get<IGameEngine>().FindRandomRequest(id);
            if (req == null)
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[RandomAnswer1] Random request not found.");
                return;
            }
            if (req.IsPhase1Completed())
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[RandomAnswer1] Too many values received. One client is buggy or tries to cheat.");
                return;
            }
            req.AddAnswer1(player, value);
            if (req.IsPhase1Completed())
                req.Answer2();
        }

        public void RandomAnswer2(IPlayPlayer player, int id, ulong value)
        {
            var req = K.C.Get<IGameEngine>().FindRandomRequest(id);
            if (req == null)
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[RandomAnswer1] Random request not found.");
                return;
            }
            req.AddAnswer2(player, value);
            if (req.IsPhase2Completed())
                req.Complete();
        }

        public void Counter(IPlayPlayer player, IPlayCounter counter, int value)
        {
            counter.SetValue(value, player, false);
        }

        public void Welcome(byte id)
        {
            K.C.Get<PlayerStateMachine>().LocalPlayer.Id = id;
            K.C.Get<Client>().StartPings();
            K.C.Get<PlayerStateMachine>().FireLocalPlayerWelcomed();
        }

        public void NewPlayer(byte id, string nick, ulong pkey)
        {
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event, "{0} has joined the game.", nick);
            var player = new Player(K.C.Get<IGameEngine>().Definition, nick, id, pkey);
            // Define the default table side if we are the host
            if (Program.IsHost)
                player.InvertedTable = (K.C.Get<PlayerStateMachine>().AllExceptGlobal.Count() & 1) == 0;
            if (Program.IsHost)
            {
                Sounds.PlaySound(Properties.Resources.knockknock);
            }
        }

        /// <summary>Loads a player deck.</summary>
        /// <param name="id">An array containing the loaded CardIdentity ids.</param>
        /// <param name="type">An array containing the corresponding CardModel guids (encrypted).</param>
        /// <param name="group">An array indicating the group the cards must be loaded into.</param>
        public void LoadDeck(int[] id, ulong[] type, IPlayGroup[] group)
        {
            if (id.Length != type.Length || id.Length != group.Length)
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[LoadDeck] Protocol violation: inconsistent arrays sizes.");
                return;
            }

            if (id.Length == 0) return;   // Loading an empty deck --> do nothing

            IPlayPlayer who = K.C.Get<PlayerStateMachine>().Find((byte)(id[0] >> 16));
            if (who == null)
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[LoadDeck] Player not found.");
                return;
            }
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(who), "{0} loads a deck.", who);
            CreateCard(id, type, group);
        }

        /// <summary>Creates new Cards as well as the corresponding CardIdentities. The cards may be in different groups.</summary>
        /// <param name="id">An array with the new CardIdentity ids.</param>
        /// <param name="type">An array containing the corresponding CardModel guids (encrypted)</param>
        /// <param name="groups">An array indicating the group the cards must be loaded into.</param>
        /// <seealso cref="CreateCard(int[], ulong[], Group)"> for a more efficient way to insert cards inside one group.</seealso>
        private static void CreateCard(IList<int> id, IList<ulong> type, IList<IPlayGroup> groups)
        {
            IPlayPlayer owner = K.C.Get<PlayerStateMachine>().Find((byte)(id[0] >> 16));
            if (owner == null)
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[CreateCard] Player not found.");
                return;
            }
            // Ignore cards created by oneself
            if (owner == K.C.Get<PlayerStateMachine>().LocalPlayer) return;
            for (int i = 0; i < id.Count; i++)
            {
                Card c = new Card(owner, id[i], type[i], null, false);
                IPlayGroup group = groups[i];
                group.AddAt(c, group.Count);
            }
        }

        /// <summary>Creates new Cards as well as the corresponding CardIdentities. All cards are created in the same group.</summary>
        /// <param name="id">An array with the new CardIdentity ids.</param>
        /// <param name="type">An array containing the corresponding CardModel guids (encrypted)</param>
        /// <param name="group">The group, in which the cards are added.</param>
        /// <seealso cref="CreateCard(int[], ulong[], Group[])"> to add cards to several groups</seealso>
        public void CreateCard(int[] id, ulong[] type, IPlayGroup group)
        {
            IPlayPlayer owner = K.C.Get<PlayerStateMachine>().Find((byte)(id[0] >> 16));
            if (owner == null)
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[CreateCard] Player not found.");
                return;
            }
            //var c = new Card(owner,id[0], type[0], Program.Game.Definition.CardDefinition, null, false);
            var c = K.C.Get<CardStateMachine>().Find(id[0]);
            
            K.C.Get<GameplayTrace>().TracePlayerEvent(owner, "{0} creates {1} {2} in {3}'s {4}", owner.Name, id.Length, c == null ? "card" : c.Name, group.Owner.Name,group.Name);
            // Ignore cards created by oneself
            if (owner == K.C.Get<PlayerStateMachine>().LocalPlayer) return;
            for (int i = 0; i < id.Length; i++)
            {
                //Card c = new Card(owner, id[i], type[i], Program.Game.Definition.CardDefinition, null, false);
                //group.AddAt(c, group.Count);
                var card = new Card(owner,id[i], type[i], null, false);
                group.AddAt(card, group.Count);
            }
        }

        /// <summary>Creates new cards on the table, as well as the corresponding CardIdentities.</summary>
        /// <param name="id">An array with the new CardIdentity ids</param>
        /// <param name="modelId"> </param>
        /// <param name="x">The x position of the cards on the table.</param>
        /// <param name="y">The y position of the cards on the table.</param>
        /// <param name="faceUp">Whether the cards are face up or not.</param>
        /// <param name="key"> </param>
        /// <param name="persist"> </param>
        public void CreateCardAt(int[] id, ulong[] key, Guid[] modelId, int[] x, int[] y, bool faceUp, bool persist)
        {
            if (id.Length == 0)
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[CreateCardAt] Empty id parameter.");
                return;
            }
            if (id.Length != key.Length || id.Length != x.Length || id.Length != y.Length || id.Length != modelId.Length)
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[CreateCardAt] Inconsistent parameters length.");
                return;
            }
            IPlayPlayer owner = K.C.Get<PlayerStateMachine>().Find((byte)(id[0] >> 16));
            if (owner == null)
            {
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[CreateCardAt] Player not found.");
                return;
            }
            var table = K.C.Get<IGameEngine>().Table;
            // Bring cards created by oneself to top, for z-order consistency
            if (owner == K.C.Get<PlayerStateMachine>().LocalPlayer)
            {
                for (int i = id.Length - 1; i >= 0; --i)
                {
                    var card = K.C.Get<CardStateMachine>().Find(id[i]);
                    if (card == null)
                    {
                        K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[CreateCardAt] Card not found.");
                        return;
                    }
                    table.SetCardIndex(card, table.Count + i - id.Length);
                }
            }
            else
            {
                for (int i = 0; i < id.Length; i++)
                    new CreateCard(owner, id[i], key[i], faceUp,K.C.Get<IGameEngine>().Definition.GetCardById(modelId[i]), x[i], y[i], !persist).Do();
            }

            // Display log messages
            try
            {
                if (modelId.All(m => m == modelId[0]))
                    K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(owner), "{0} creates {1} '{2}'", owner, modelId.Length, owner == K.C.Get<PlayerStateMachine>().LocalPlayer || faceUp ? K.C.Get<IGameEngine>().Definition.GetCardById(modelId[0]).Name : "card");
                else
                    foreach (Guid m in modelId)
                        K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(owner), "{0} creates a '{1}'", owner, owner == K.C.Get<PlayerStateMachine>().LocalPlayer || faceUp ? K.C.Get<IGameEngine>().Definition.GetCardById(m).Name : "card");

            }
            catch (Exception e)
            {
                // TODO - [FIX THIS SHIT] - A null reference exception happens on the first trace event. - Kelly Elton - 3/24/2013
                // This should be cleaered up, this is only a temp fix. - Kelly Elton - 3/24/2013
            }
        }

        /// <summary>Create new CardIdentities, which hide aliases to other CardIdentities</summary>
        /// <param name="id">An array containing the new CardIdentity ids</param>
        /// <param name="type">An array with the aliased CardIdentity ids (encrypted)</param>
        public void CreateAlias(int[] id, ulong[] type)
        {
            byte playerId = (byte)(id[0] >> 16);
            // Ignore cards created by oneself
            if (playerId == K.C.Get<PlayerStateMachine>().LocalPlayer.Id) return;
            for (int i = 0; i < id.Length; i++)
            {
                if (type[i] == ulong.MaxValue) continue;
                CardIdentity ci = new CardIdentity(id[i]) {Alias = true, Key = type[i]};
            }
        }

        public void Leave(IPlayPlayer player)
        {
            player.Delete();
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event, "{0} has left the game.", player);
            if (Program.IsHost)
            {
                Sounds.PlaySound(Properties.Resources.doorclose);
            }
        }

        public void MoveCard(IPlayPlayer player, IPlayCard card, IPlayGroup to, int idx, bool faceUp)
        {
            // Ignore cards moved by the local player (already done, for responsiveness)
            if (player != K.C.Get<PlayerStateMachine>().LocalPlayer)
                new MoveCard(player, card, to, idx, faceUp).Do();
            else
            {
                // Fix: cards may move quickly locally from one group to another one, before we get a chance
                // to execute this handler, during game actions scripts (e.g. Mulligan with one player -
                // shuffling happens locally). The result is that we are going to receive two messages,
                // one for the move to group A, then the move to group B; while the card already is in group B.
                // In this case, trying to set index inside group B with an index coming from the time the card
                // was in group A is just plain wrong and may crash depending on the index.
                if (card.Group == to)
                    card.SetIndex(idx); // This is done to preserve stack order consistency with other players (should be a noop most of the time)
            }
        }

        public void MoveCardAt(IPlayPlayer player, IPlayCard card, int x, int y, int idx, bool faceUp)
        {
            // Get the table control
            IPlayTable table = K.C.Get<IGameEngine>().Table;
            // Because every player may manipulate the table at the same time, the index may be out of bound
            if (card.Group == table)
            { if (idx >= table.Count) idx = table.Count - 1; }
            else
                if (idx > table.Count) idx = table.Count;

            // Ignore cards moved by the local player (already done, for responsiveness)
            if (player == K.C.Get<PlayerStateMachine>().LocalPlayer)
            {
                // See remark in MoveCard
                if (card.Group == table)
                    card.SetIndex(idx); // This is done to preserve stack order consistency with other players (should be a noop most of the time)
                return;
            }
            // Find the old position on the table, if any
            //bool onTable = card.Group == table;
            //double oldX = card.X, oldY = card.Y;
            // Do the move
            new MoveCard(player, card, x, y, idx, faceUp).Do();
        }

        public void SetMarker(IPlayPlayer player, IPlayCard card, Guid id, string name, ushort count)
        {
            // Always perform this call (even if player == LocalPlayer) for consistency as markers aren't an exclusive resource
            card.SetMarker(player, id, name, count);
        }

        public void AddMarker(IPlayPlayer player, IPlayCard card, Guid id, string name, ushort count)
        {
            DataNew.Entities.Marker model = K.C.Get<IGameEngine>().GetMarkerModel(id);
            DefaultMarkerModel defaultMarkerModel = model as DefaultMarkerModel;
            if (defaultMarkerModel != null)
                (defaultMarkerModel).SetName(name);
            // Ignore markers created by oneself (already created for responsiveness issues)
            if (player != K.C.Get<PlayerStateMachine>().LocalPlayer)
                card.AddMarker(model, count);
            if (count != 0)
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(player),
                  "{0} adds {1} {2} marker(s) on {3}", player, count, model, card);
        }

        public void RemoveMarker(IPlayPlayer player, IPlayCard card, Guid id, string name, ushort count)
        {
            // Ignore markers removed by oneself (already removed for responsiveness issues)
            if (player != K.C.Get<PlayerStateMachine>().LocalPlayer)
            {
                PlayMarker marker = card.FindMarker(id, name);
                if (marker == null)
                {
                    K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.NonGame, "Inconsistent state. Marker not found on card.");
                    return;
                }
                if (marker.Count < count)
                    K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.NonGame, "Inconsistent state. Missing markers to remove");

                card.RemoveMarker(marker, count);
            }
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(player), "{0} removes {1} {2} marker(s) from {3}", player, count, name, card);
        }

        public void TransferMarker(IPlayPlayer player, IPlayCard from, IPlayCard to, Guid id, string name, ushort count)
        {
            // Ignore markers moved by oneself (already moved for responsiveness issues)
            if (player != K.C.Get<PlayerStateMachine>().LocalPlayer)
            {
                PlayMarker marker = from.FindMarker(id, name);
                if (marker == null)
                {
                    K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.NonGame, "Inconsistent state. Marker not found on card.");
                    return;
                }
                if (marker.Count < count)
                    K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.NonGame, "Inconsistent state. Missing markers to remove");

                from.RemoveMarker(marker, count);
                to.AddMarker(marker.Model, count);
            }
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(player), "{0} moves {1} {2} marker(s) from {3} to {4}", player, count, name, from, to);
        }

        public void Nick(IPlayPlayer player, string nick)
        {
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event, "{0} is now known as {1}.", player, nick);
            player.Name = nick;
        }

        /// <summary>Reveal one card's identity</summary>
        /// <param name="card">The card, whose identity is revealed</param>
        /// <param name="revealed">Either the salted CardIdentity id (in the case of an alias), or the salted, condensed Card GUID.</param>
        /// <param name="guid"> </param>
        public void Reveal(IPlayCard card, ulong revealed, Guid guid)
        {
            // Save old id
            CardIdentity oldType = card.Type;
            // Check if the card is rightfully revealed
            if (!card.Type.Revealing)
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "Someone tries to reveal a card which is not visible to everybody.");
            // Check if we can trust other clients
            if (!card.Type.MySecret)
            {
                if (guid != Guid.Empty && (uint)revealed != guid.Condense())
                    K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[Reveal] Alias and id aren't the same. One client is buggy or tries to cheat.");
                if (Crypto.ModExp(revealed) != card.Type.Key)
                    K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[Reveal] Card identity doesn't match. One client is buggy or tries to cheat.");
            }
            else
                card.Type.MySecret = false;
            // Reveal an alias
            if (guid == Guid.Empty)
            {
                // Find the new type
                CardIdentity newId = CardIdentity.Find((int)revealed);
                // HACK: it is unclear to me how the CardIdentity could not be found and newId ends up null
                // see this bug report: https://octgn.16bugs.com/projects/3602/bugs/192070
                // for now I'm just doing nothing (supposing that it means the type was already revealed).
                if (newId == null) { card.Reveal(); return; }
                // Possibly copy the model, if it was known and isn't anymore
                // (possible when the alias has beeen locally revealed)
                if (newId.Model == null) newId.Model = card.Type.Model;
                // Set the new type
                card.Type = newId;
                // Delete the old identity
                CardIdentity.Delete(oldType.Id);
                // Possibly reveal the alias further
                card.Reveal();
                // Raise a notification
                oldType.OnRevealed(newId);
            }
            // Reveal a card's type
            else if (card.Type.Model == null)
            {
                card.SetModel(K.C.Get<IGameEngine>().Definition.GetCardById(guid));
                // Raise a notification
                oldType.OnRevealed(oldType);
            }
        }

        /// <summary>Reveal a card's identity to one player only.</summary>
        /// <param name="players"> </param>
        /// <param name="card">The card, whose identity is revealed.</param>
        /// <param name="encrypted">Either a ulong[2] containing an encrypted aliased CardIdentity id. Or a ulong[5] containing an encrypted CardModel guid.</param>
        public void RevealTo(IPlayPlayer[] players, IPlayCard card, ulong[] encrypted)
        {
            var oldType = card.Type;
            ulong alias = 0;
            Guid id = Guid.Empty;

            switch (encrypted.Length)
            {
                case 2:
                    alias = Crypto.Decrypt(encrypted);
                    break;
                case 5:
                    id = Crypto.DecryptGuid(encrypted);
                    break;
                default:
                    K.C.Get<GameplayTrace>().TraceWarning("[RevealTo] Invalid data received.");
                    return;
            }

            if (!players.All(p => (card.Group.Visibility == DataNew.Entities.GroupVisibility.Custom && card.Group.Viewers.Contains(p)) ||
                                  card.PlayersLooking.Contains(p) || card.PeekingPlayers.Contains(p)))
                K.C.Get<GameplayTrace>().TraceWarning("[RevealTo] Revealing a card to a player, who isn't allowed to see it. This indicates a bug or cheating.");

            // If it's an alias, we must revealed it to the final recipient
            bool sendToMyself = true;
            if (alias != 0)
            {
                sendToMyself = false;
                CardIdentity ci = CardIdentity.Find((int)alias);
                if (ci == null)
                { K.C.Get<GameplayTrace>().TraceWarning("[RevealTo] Identity not found."); return; }

                // If the revealed type is an alias, pass it to the one who owns it to continue the RevealTo chain.
                if (ci.Alias)
                {
                    IPlayPlayer p = K.C.Get<PlayerStateMachine>().Find((byte)(ci.Key >> 16));
                    K.C.Get<Client>().Rpc.RevealToReq(p, players, card, Crypto.Encrypt(ci.Key, p.PublicKey));
                }
                // Else revealed the card model to the ones, who must see it
                else
                {
                    IPlayPlayer[] pArray = new IPlayPlayer[1];
                    foreach (IPlayPlayer p in players)
                        if (p != K.C.Get<PlayerStateMachine>().LocalPlayer)
                        {
                            pArray[0] = p;
                            K.C.Get<Client>().Rpc.RevealToReq(p, pArray, card, Crypto.Encrypt(ci.Model.Id, p.PublicKey));
                        }
                        else
                        {
                            sendToMyself = true;
                            id = ci.Model.Id;
                        }
                }
            }
            // Else it's a type and we are the final recipients
            if (!sendToMyself) return;
            if (card.Type.Model == null)
                card.SetModel(K.C.Get<IGameEngine>().Definition.GetCardById(id));
            // Raise a notification
            oldType.OnRevealed(card.Type);
        }

        public void Peek(IPlayPlayer player, IPlayCard card)
        {
            if (!card.PeekingPlayers.Contains(player))
                card.PeekingPlayers.Add(player);
            card.RevealTo(Enumerable.Repeat(player, 1));
            if (player != K.C.Get<PlayerStateMachine>().LocalPlayer)
            {
                K.C.Get<GameplayTrace>().TracePlayerEvent(player, "{0} peeks at a card ({1}).", player,
                  card.Group is IPlayTable ? "on table" : "in " + card.Group.FullName);
            }
        }

        public void Untarget(IPlayPlayer player, IPlayCard card)
        {
            // Ignore the card we targeted ourselves
            if (player == K.C.Get<PlayerStateMachine>().LocalPlayer) return;
            new Target(player, card, null, false).Do();
        }

        public void Target(IPlayPlayer player, IPlayCard card)
        {
            // Ignore the card we targeted ourselves
            if (player == K.C.Get<PlayerStateMachine>().LocalPlayer) return;
            new Target(player, card, null, true).Do();
        }

        public void TargetArrow(IPlayPlayer player, IPlayCard card, IPlayCard otherCard)
        {
            // Ignore the card we targeted ourselves
            if (player == K.C.Get<PlayerStateMachine>().LocalPlayer) return;
            new Target(player, card, otherCard, true).Do();
        }

        public void Highlight(IPlayCard card, Color? color)
        { card.SetHighlight(color); }

        public void Turn(IPlayPlayer player, IPlayCard card, bool up)
        {
            // Ignore the card we turned ourselves
            if (player == K.C.Get<PlayerStateMachine>().LocalPlayer)
            {
                card.MayBeConsideredFaceUp = false;     // see comment on mayBeConsideredFaceUp
                return;
            }
            new Turn(player, card, up).Do();
        }

        public void Rotate(IPlayPlayer player, IPlayCard card, CardOrientation rot)
        {
            // Ignore the moves we made ourselves
            if (player == K.C.Get<PlayerStateMachine>().LocalPlayer)
                return;
            new Rotate(player, card, rot).Do();
        }

        /// <summary>Part of a shuffle process.</summary>
        /// <param name="group">The group being shuffled.</param>
        /// <param name="card">An array containing the CardIdentity ids to shuffle.</param>
        public void Shuffle(IPlayGroup group, int[] card)
        {
            // Array to hold the new aliases (sent to CreateAlias)
            ulong[] aliases = new ulong[card.Length];
            // Intialize the group shuffle
            group.FilledShuffleSlots = 0;
            group.HasReceivedFirstShuffledMessage = false;
            group.MyShufflePos = new short[card.Length];
            // Check if we received enough cards
            if (card.Length < group.Count / (K.C.Get<PlayerStateMachine>().Count - 1))
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.Event, "[Shuffle] Too few cards received.");
            // Do the shuffling
            var rnd = new CryptoRandom();
            for (int i = card.Length - 1; i >= 0; i--)
            {
                int r = rnd.Next(i + 1);
                int tc = card[r];
                card[r] = card[i];
                // Create a new alias, if the card is not face up
                CardIdentity ci = CardIdentity.Find(tc);
                if (group.FindByCardIdentity(ci) != null)
                {
                    card[i] = tc; aliases[i] = ulong.MaxValue;
                    ci.Visible = true;
                }
                else
                {
                    ci = new CardIdentity(K.C.Get<IGameEngine>().GenerateCardId());
                    ci.MySecret = ci.Alias = true;
                    ci.Key = ((ulong)Crypto.PositiveRandom()) << 32 | (uint)tc;
                    card[i] = ci.Id; aliases[i] = Crypto.ModExp(ci.Key);
                    ci.Visible = false;
                }
                // Give a random position to the card
                group.MyShufflePos[i] = (short)Crypto.Random(group.Count);
            }
            // Send the results
            K.C.Get<Client>().Rpc.CreateAlias(card, aliases);
            K.C.Get<Client>().Rpc.Shuffled(group, card, group.MyShufflePos);
        }

        public void Shuffled(IPlayGroup group, int[] card, short[] pos)
        {
            // Check the args
            if (card.Length != pos.Length)
            {
                K.C.Get<GameplayTrace>().TraceWarning("[Shuffled] Cards and positions lengths don't match.");
                return;
            }
            group.FilledShuffleSlots += card.Length;
            if (group.FilledShuffleSlots > group.Count)
            {
                K.C.Get<GameplayTrace>().TraceWarning("[Shuffled] Too many card positions received.");
                return;
            }
            // If it's the first packet we receive for this shuffle, clear all Types
            if (!group.HasReceivedFirstShuffledMessage)
                foreach (IPlayCard c in group) c.Type = null;
            group.HasReceivedFirstShuffledMessage = true;
            // Check that the server didn't change our positions
            if (card[0] >> 16 == K.C.Get<PlayerStateMachine>().LocalPlayer.Id && group.MyShufflePos != null)
            {
                if (pos.Where((t, i) => t != @group.MyShufflePos[i]).Any())
                {
                    K.C.Get<GameplayTrace>().TraceWarning("[Shuffled] The server has changed the order of the cards.");
                }
                group.MyShufflePos = null;
            }
            // Insert the cards
            for (int j = 0; j < card.Length; j++)
            {
                // Get the wished position
                int i = pos[j];
                // Get the card
                CardIdentity ci = CardIdentity.Find(card[j]);
                if (ci == null)
                {
                    K.C.Get<GameplayTrace>().TraceWarning("[Shuffled] Card not found.");
                    continue;
                }
                // Check if the slot is free, otherwise choose the first free one
                if (i >= group.Count || group[i].Type != null) i = group.FindNextFreeSlot(i);
                if (i >= group.Count) continue;
                // Set the type
                group[i].Type = ci;
                group[i].SetVisibility(ci.Visible ? DataNew.Entities.GroupVisibility.Everybody : DataNew.Entities.GroupVisibility.Nobody, null);
            }
            if (group.FilledShuffleSlots == group.Count)
                group.OnShuffled();
        }

        /// <summary>Completely remove all aliases from a group, e.g. before performing a shuffle.</summary>
        /// <param name="group">The group to remove all aliases from.</param>
        public void UnaliasGrp(IPlayGroup group)
        {
            // Get the group
            Pile g = group as Pile;
            if (g == null)
            { K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Warning, EventIds.NonGame, "[UnaliasGrp] Group is not a pile."); return; }
            // Collect aliases which we p
            List<int> cards = new List<int>(g.Count);
            List<ulong> types = new List<ulong>(g.Count);
            bool hasAlias = false;
            foreach (IPlayCard t in g)
            {
                CardIdentity ci = t.Type;
                if (!ci.Alias) continue;
                hasAlias = true;
                if (ci.MySecret)
                { cards.Add(t.Id); types.Add(ci.Key); }
            }
            // Unalias cards that we know (if any)
            if (cards.Count > 0)
                K.C.Get<Client>().Rpc.Unalias(cards.ToArray(), types.ToArray());
            // If there are no alias, we may be ready to shuffle
            if (!hasAlias && g.WantToShuffle)
            { g.DoShuffle(); return; }
            // Mark the group for shuffling
            g.PreparingShuffle = true;
            // Notify the user
            K.C.Get<GameplayTrace>().TracePlayerEvent(group.Owner, "{0} is being prepared for shuffle.", g);
            // Check for null because the chat can currently be muted (e.g. during a Mulligan scripted action)
            if (Program.LastChatTrace != null)
                g.ShuffledTrace += (new ShuffleTraceChatHandler { Line = Program.LastChatTrace }).ReplaceText;
        }

        /// <summary>Unalias some Cards, e.g. before a shuffle</summary>
        /// <param name="card">An array containing the Card ids to unalias.</param>
        /// <param name="type">An array containing the corresponding revealed CardIdentity ids.</param>
        public void Unalias(int[] card, ulong[] type)
        {
            if (card.Length != type.Length)
            { K.C.Get<GameplayTrace>().TraceWarning("[Unalias] Card and type lengths don't match."); return; }
            Pile g = null;
            List<int> cards = new List<int>(card.Length);
            List<ulong> types = new List<ulong>(card.Length);
            for (int i = 0; i < card.Length; i++)
            {
                IPlayCard c = K.C.Get<CardStateMachine>().Find(card[i]);
                if (c == null)
                { K.C.Get<GameplayTrace>().TraceWarning("[Unalias] Card not found."); continue; }
                if (g == null) g = c.Group as Pile;
                else if (g != c.Group)
                { K.C.Get<GameplayTrace>().TraceWarning("[Unalias] Not all cards belong to the same group!"); continue; }
                // Check nobody cheated
                if (!c.Type.MySecret)
                {
                    if (c.Type.Key != Crypto.ModExp(type[i]))
                        K.C.Get<GameplayTrace>().TraceWarning("[Unalias] Card identity doesn't match.");
                }
                // Substitue the card's identity
                CardIdentity ci = CardIdentity.Find((int)type[i]);
                if (ci == null)
                { K.C.Get<GameplayTrace>().TraceWarning("[Unalias] Card identity not found."); continue; }
                CardIdentity.Delete(c.Type.Id); c.Type = ci;
                // Propagate unaliasing
                if (ci.Alias && ci.MySecret)
                    cards.Add(c.Id); types.Add(ci.Key);
            }
            if (cards.Count > 0)
                K.C.Get<Client>().Rpc.Unalias(cards.ToArray(), types.ToArray());
            if (g == null) return;
            if (!g.PreparingShuffle)
            { K.C.Get<GameplayTrace>().TraceWarning("[Unalias] Cards revealed are not in a group prepared for shuffle."); return; }
            // If all cards are now revealed, one can proceed to shuffling
            if (!g.WantToShuffle) return;
            bool done = false;
            for (int i = 0; !done && i < g.Count; i++)
                done = g[i].Type.Alias;
            if (!done)
                g.DoShuffle();
        }

        public void PassTo(IPlayPlayer who, IPlayControllableObject obj, IPlayPlayer player, bool requested)
        {
            // Ignore message that we sent in the first place
            if (who != K.C.Get<PlayerStateMachine>().LocalPlayer)
                obj.PassControlTo(player, who, false, requested);
        }

        public void TakeFrom(IPlayControllableObject obj, IPlayPlayer to)
        { obj.TakingControl(to); }

        public void DontTake(IPlayControllableObject obj)
        { obj.DontTakeError(); }

        public void FreezeCardsVisibility(IPlayGroup group)
        {
            foreach (IPlayCard c in group.Cards) c.SetOverrideGroupVisibility(true);
        }

        public void GroupVis(IPlayPlayer player, IPlayGroup group, bool defined, bool visible)
        {
            // Ignore messages sent by myself
            if (player != K.C.Get<PlayerStateMachine>().LocalPlayer)
                group.SetVisibility(defined ? (bool?)visible : null, false);
            if (defined)
                K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(player), visible ? "{0} shows {1} to everybody." : "{0} shows {1} to nobody.", player, group);
        }

        public void GroupVisAdd(IPlayPlayer player, IPlayGroup group, IPlayPlayer whom)
        {
            // Ignore messages sent by myself
            if (player != K.C.Get<PlayerStateMachine>().LocalPlayer)
                group.AddViewer(whom, false);
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(player), "{0} shows {1} to {2}.", player, group, whom);
        }

        public void GroupVisRemove(IPlayPlayer player, IPlayGroup group, IPlayPlayer whom)
        {
            // Ignore messages sent by myself
            if (player != K.C.Get<PlayerStateMachine>().LocalPlayer)
                group.RemoveViewer(whom, false);
            K.C.Get<GameplayTrace>().TraceEvent(TraceEventType.Information, EventIds.Event | EventIds.PlayerFlag(player), "{0} hides {1} from {2}.", player, group, whom);
        }

        public void LookAt(IPlayPlayer player, int uid, IPlayGroup group, bool look)
        {
            if (look)
            {
                if (group.Visibility != DataNew.Entities.GroupVisibility.Everybody)
                    foreach (IPlayCard c in group)
                    {
                        c.PlayersLooking.Add(player);
                        c.RevealTo(Enumerable.Repeat(player, 1));
                    }
                group.LookedAt.Add(uid, group.ToList());
                K.C.Get<GameplayTrace>().TracePlayerEvent(player, "{0} looks at {1}.", player, group);
            }
            else
            {
                if (!group.LookedAt.ContainsKey(uid))
                { K.C.Get<GameplayTrace>().TraceWarning("[LookAtTop] Protocol violation: unknown unique id received."); return; }
                if (group.Visibility != DataNew.Entities.GroupVisibility.Everybody)
                {
                    foreach (IPlayCard c in group.LookedAt[uid])
                        c.PlayersLooking.Remove(player);
                }
                group.LookedAt.Remove(uid);
                K.C.Get<GameplayTrace>().TracePlayerEvent(player, "{0} stops looking at {1}.", player, group);
            }
        }

        public void LookAtTop(IPlayPlayer player, int uid, IPlayGroup group, int count, bool look)
        {
            if (look)
            {
                var cards = group.Take(count);
                foreach (IPlayCard c in cards)
                {
                    c.PlayersLooking.Add(player);
                    c.RevealTo(Enumerable.Repeat(player, 1));
                }
                group.LookedAt.Add(uid, cards.ToList());
                K.C.Get<GameplayTrace>().TracePlayerEvent(player, "{0} looks at {1} top {2} cards.", player, group, count);
            }
            else
            {
                if (!group.LookedAt.ContainsKey(uid))
                { K.C.Get<GameplayTrace>().TraceWarning("[LookAtTop] Protocol violation: unknown unique id received."); return; }
                foreach (IPlayCard c in group.LookedAt[uid])
                    c.PlayersLooking.Remove(player);
                K.C.Get<GameplayTrace>().TracePlayerEvent(player, "{0} stops looking at {1} top {2} cards.", player, group, count);
                group.LookedAt.Remove(uid);
            }
        }

        public void LookAtBottom(IPlayPlayer player, int uid, IPlayGroup group, int count, bool look)
        {
            if (look)
            {
                int skipCount = Math.Max(0, group.Count - count);
                var cards = group.Skip(skipCount);
                foreach (IPlayCard c in cards)
                {
                    c.PlayersLooking.Add(player);
                    c.RevealTo(Enumerable.Repeat(player, 1));
                }
                group.LookedAt.Add(uid, cards.ToList());
                K.C.Get<GameplayTrace>().TracePlayerEvent(player, "{0} looks at {1} bottom {2} cards.", player, group, count);
            }
            else
            {
                if (!group.LookedAt.ContainsKey(uid))
                { K.C.Get<GameplayTrace>().TraceWarning("[LookAtTop] Protocol violation: unknown unique id received."); return; }
                foreach (IPlayCard c in group.LookedAt[uid])
                    c.PlayersLooking.Remove(player);
                K.C.Get<GameplayTrace>().TracePlayerEvent(player, "{0} stops looking at {1} bottom {2} cards.", player, group, count);
                group.LookedAt.Remove(uid);
            }
        }

        public void StartLimited(IPlayPlayer player, Guid[] packs)
        {
            K.C.Get<GameplayTrace>().TracePlayerEvent(player, "{0} starts a limited game.", player);
            var wnd = new Play.Dialogs.PickCardsDialog();
            WindowManager.PlayWindow.ShowBackstage(wnd);
            wnd.OpenPacks(packs);
        }

        public void CancelLimited(IPlayPlayer player)
        {
            K.C.Get<GameplayTrace>().TracePlayerEvent(player, "{0} cancels out of the limited game.", player);
        }

        public void PlayerSetGlobalVariable(IPlayPlayer p, string name, string value)
        {
            if (p.GlobalVariables.ContainsKey(name))
                p.GlobalVariables[name] = value;
            else
                p.GlobalVariables.Add(name, value);
        }

        public void SetGlobalVariable(string name, string value)
        {
            if (K.C.Get<IGameEngine>().GlobalVariables.ContainsKey(name))
                K.C.Get<IGameEngine>().GlobalVariables[name] = value;
            else
                K.C.Get<IGameEngine>().GlobalVariables.Add(name, value);
        }

        public void IsTableBackgroundFlipped(bool isFlipped)
        {
            K.C.Get<IGameEngine>().IsTableBackgroundFlipped = isFlipped;
        }

        public void CardSwitchTo(IPlayPlayer player, IPlayCard card, string alternate)
        {
            if(player.Id != K.C.Get<PlayerStateMachine>().LocalPlayer.Id)
                card.SwitchTo(player, alternate);
        }

        public void Ping()
        {
            
        }
    }
}