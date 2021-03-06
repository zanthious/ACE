﻿using System;

using ACE.Common;
using ACE.Common.Cryptography;
using ACE.Database;
using ACE.Entity;
using ACE.Managers;
using ACE.Network.Enum;
using ACE.Network.GameMessages.Messages;
using ACE.Network.Packets;
using System.Collections.Generic;
using System.Linq;
using log4net;

namespace ACE.Network.Handlers
{
    public static class AuthenticationHandler
    {
        /// <summary>
        /// Seconds until an authentication request will timeout/expire.
        /// </summary>
        public const int DefaultAuthTimeout = 15;

        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static async void HandleLoginRequest(ClientPacket packet, Session session)
        {
            PacketInboundLoginRequest loginRequest = new PacketInboundLoginRequest(packet);

            try
            {
                var result = await DatabaseManager.Authentication.GetAccountByName(loginRequest.Account);
                AccountSelectCallback(result, session);
            }
            catch (IndexOutOfRangeException)
            {
                AccountSelectCallback(null, session);
            }
        }

        private static void AccountSelectCallback(Account account, Session session)
        {
            log.DebugFormat("ConnectRequest TS: {0}", session.Network.ConnectionData.ServerTime);
            var connectRequest = new PacketOutboundConnectRequest(session.Network.ConnectionData.ServerTime, 0, session.Network.ClientId, ISAAC.ServerSeed, ISAAC.ClientSeed);

            session.Network.EnqueueSend(connectRequest);

            if (account == null)
            {
                session.SendCharacterError(CharacterError.AccountDoesntExist);
                return;
            }

            if (WorldManager.Find(account.Name) != null)
            {
                var foundSession = WorldManager.Find(account.Name);

                if (foundSession.State == SessionState.AuthConnected)
                    session.SendCharacterError(CharacterError.AccountInUse);
                return;
            }

            /*if (glsTicket != digest)
            {
            }*/

            // TODO: check for account bans

            session.SetAccount(account.AccountId, account.Name, account.AccessLevel);
            session.State = SessionState.AuthConnectResponse;
        }

        public static void HandleConnectResponse(ClientPacket packet, Session session)
        {
            PacketInboundConnectResponse connectResponse = new PacketInboundConnectResponse(packet);

            DatabaseManager.Shard.GetCharacters(session.Id, ((List<CachedCharacter> result) =>
            {
                result = result.OrderByDescending(o => o.LoginTimestamp).ToList();
                session.UpdateCachedCharacters(result);

                GameMessageCharacterList characterListMessage = new GameMessageCharacterList(result, session.Account);
                GameMessageServerName serverNameMessage = new GameMessageServerName(ConfigManager.Config.Server.WorldName, WorldManager.GetAll().Count, (int)ConfigManager.Config.Server.Network.MaximumAllowedSessions);
                GameMessageDDDInterrogation dddInterrogation = new GameMessageDDDInterrogation();

                session.Network.EnqueueSend(characterListMessage, serverNameMessage);
                session.Network.EnqueueSend(dddInterrogation);

                session.State = SessionState.AuthConnected;
            }));
        }
    }
}
