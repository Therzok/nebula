﻿using NebulaModel.Logger;
using NebulaModel.Networking;
using NebulaModel.Networking.Serialization;
using NebulaModel.Packets.Session;
using NebulaModel.Utils;
using NebulaWorld;
using UnityEngine;
using WebSocketSharp;

namespace NebulaClient
{
    public class MultiplayerClientSession : MonoBehaviour, INetworkProvider
    {
        public static MultiplayerClientSession Instance { get; protected set; }

        private WebSocket clientSocket;
        private NebulaConnection serverConnection;

        public NetPacketProcessor PacketProcessor { get; protected set; }
        public bool IsConnected { get; protected set; }

        private string serverIp;
        private int serverPort;

        private void Awake()
        {
            Instance = this;
        }

        public void Connect(string ip, int port)
        {
            serverIp = ip;
            serverPort = port;

            clientSocket = new WebSocket($"ws://{ip}:{port}/socket");
            clientSocket.OnOpen += ClientSocket_OnOpen;
            clientSocket.OnClose += ClientSocket_OnClose;
            clientSocket.OnMessage += ClientSocket_OnMessage;

            PacketProcessor = new NetPacketProcessor();
#if DEBUG
            PacketProcessor.SimulateLatency = true;
#endif

            PacketUtils.RegisterAllPacketNestedTypes(PacketProcessor);
            PacketUtils.RegisterAllPacketProcessorsInCallingAssembly(PacketProcessor);

            clientSocket.Connect();

            SimulatedWorld.Initialize();

            LocalPlayer.IsMasterClient = false;
            LocalPlayer.SetNetworkProvider(this);
        }

        void Disconnect()
        {
            IsConnected = false;
            clientSocket.Close((ushort)NebulaStatusCode.ClientRequestedDisconnect, "Player left the game");
        }

        public void DestroySession()
        {
            Disconnect();
            Destroy(gameObject);
        }

        public void SendPacket<T>(T packet) where T : class, new()
        {
            serverConnection?.SendPacket(packet);
        }

        public void Reconnect()
        {
            SimulatedWorld.Clear();
            Disconnect();
            Connect(serverIp, serverPort);
        }

        private void ClientSocket_OnMessage(object sender, MessageEventArgs e)
        {
            PacketProcessor.EnqueuePacketForProcessing(e.RawData, new NebulaConnection(clientSocket, PacketProcessor));
        }

        private void ClientSocket_OnOpen(object sender, System.EventArgs e)
        {
            Log.Info($"Server connection established: {clientSocket.Url}");
            serverConnection = new NebulaConnection(clientSocket, PacketProcessor);
            IsConnected = true;
            SendPacket(new HandshakeRequest());
        }

        private void ClientSocket_OnClose(object sender, CloseEventArgs e)
        {
            IsConnected = false;
            serverConnection = null;

            // If the client is Quitting by himself, we don't have to inform him of his disconnection.
            if (e.Code == (ushort)NebulaStatusCode.ClientRequestedDisconnect)
                return;

            if (SimulatedWorld.IsGameLoaded)
            {
                UnityDispatchQueue.RunOnMainThread(() =>
                {
                    InGamePopup.ShowWarning(
                        "Connection Lost",
                        $"You have been disconnect of the server.\n{e.Reason}",
                        "Quit", "Reconnect",
                        () => { LocalPlayer.LeaveGame(); },
                        () => { Reconnect(); });
                });
            }
            else
            {
                UnityDispatchQueue.RunOnMainThread(() =>
                {
                    InGamePopup.ShowWarning(
                        "Server Unavailable",
                        $"Could not reach the server, please try again later.",
                        "OK",
                        () =>
                        {
                            GameObject overlayCanvasGo = GameObject.Find("Overlay Canvas");
                            Transform multiplayerMenu = overlayCanvasGo?.transform?.Find("Nebula - Multiplayer Menu");
                            multiplayerMenu?.gameObject?.SetActive(true);
                        });
                });
            }
        }

        private void Update()
        {
            PacketProcessor.ProcessPacketQueue();
        }
    }
}
