using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System;
using System.Text;
using System.Collections.Generic;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    private NativeList<NetworkConnection> m_Connections;

    public Dictionary<string,NetworkObjects.NetworkPlayer> m_players;
    private Dictionary<string, float> m_timeOfLast;

    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = serverPort;
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);
        else
            m_Driver.Listen();

        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
        m_players = new Dictionary<string, NetworkObjects.NetworkPlayer>();
        m_timeOfLast = new Dictionary<string, float>();
        InvokeRepeating("checkConnection", 5.0f, 5.0f);
    }

    void SendToClient(string message, NetworkConnection c){
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }
    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    void OnConnect(NetworkConnection c){
        m_Connections.Add(c);
        Debug.Log("Accepted a connection");

        ////Example to send a handshake message:
        HandshakeMsg m = new HandshakeMsg();
        m.player.id = c.InternalId.ToString();
        SendToClient(JsonUtility.ToJson(m),c);
    }

    void OnData(DataStreamReader stream, int i){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch (header.cmd){
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                m_players.Add(hsMsg.player.id, hsMsg.player);
                m_timeOfLast.Add(hsMsg.player.id, System.DateTime.Now.Second);
                print("recived handshake");
            break;
            case Commands.PLAYER_UPDATE:

                print("recived client update");
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);

                m_timeOfLast[puMsg.player.id] = System.DateTime.Now.Second;

                m_players[puMsg.player.id] = puMsg.player;

                ServerUpdateMsg m = new ServerUpdateMsg();
                List<NetworkObjects.NetworkPlayer> playerList = new List<NetworkObjects.NetworkPlayer>();
                foreach (KeyValuePair<string,NetworkObjects.NetworkPlayer> dictionaryItem in m_players) 
                {
                    playerList.Add(dictionaryItem.Value);
                }
                m.players = playerList;
                SendToClient(JsonUtility.ToJson(m), m_Connections[i]);

                break;
            case Commands.SERVER_UPDATE:
            ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
            Debug.Log("Server update message received!");
            break;
            default:
            Debug.Log("SERVER ERROR: Unrecognized message received!");
            break;
        }
    }

    void OnDisconnect(int i){
        Debug.Log("Client disconnected from server");
        m_players.Remove(m_Connections[i].InternalId.ToString());
        m_timeOfLast.Remove(m_Connections[i].InternalId.ToString());
        m_Connections[i] = default(NetworkConnection);
    }

    void Update ()
    {
        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {

                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        // AcceptNewConnections
        NetworkConnection c = m_Driver.Accept();
        while (c  != default(NetworkConnection))
        {            
            OnConnect(c);

            // Check if there is another new connection
            c = m_Driver.Accept();
        }


        // Read Incoming Messages
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            
            NetworkEvent.Type cmd;
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            while (cmd != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    OnData(stream, i);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
    }

    void checkConnection()
     {
         for (int i = 0; i < m_Connections.Length; i++)
         {
             if (System.DateTime.Now.Second - m_timeOfLast[m_Connections[i].InternalId.ToString()] >  5.0f)
             {
                 OnDisconnect(i);
             }
         }
     }

}