using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using Unity.Networking.Transport;
using NetworkMessages;
using NetworkObjects;
using System;
using System.Text;

public class NetworkClient : MonoBehaviour
{

    public GameObject playerObject;

    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;

    private NetworkObjects.NetworkPlayer m_player = new NetworkObjects.NetworkPlayer();

    private List<GameObject> m_Players;

    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP,serverPort);
        m_Connection = m_Driver.Connect(endpoint);

        m_Players = new List<GameObject>();
    }
    
    void SendToServer(string message){
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }

    void OnConnect(){
        Debug.Log("We are now connected to the server");

        HandshakeMsg m = new HandshakeMsg();
        m_player.id = m_Connection.InternalId.ToString();
        m_player.cubePos = new Vector3(0.0f, 0.0f, 0.0f);
        m_player.cubeColor = new Color(UnityEngine.Random.Range(0.0f, 1.0f), UnityEngine.Random.Range(0.0f, 1.0f), UnityEngine.Random.Range(0.0f, 1.0f), 1.0f);
        m.player = m_player;
        SendToServer(JsonUtility.ToJson(m));

        //InvokeRepeating("sendHeatBeat", 0.033f, 0.033f);
        InvokeRepeating("sendHeatBeat", 1.0f, 1.0f);
    }

    void OnData(DataStreamReader stream){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
            HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
            Debug.Log("Handshake message received!");
            break;
            case Commands.PLAYER_UPDATE:
            PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
            Debug.Log("Player update message received!");
            break;
            case Commands.SERVER_UPDATE:
                
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                spawnCubes(suMsg.players.Count);
                for (int i = 0; i < suMsg.players.Count; i++)
                {
                    m_Players[i].GetComponent<Transform>().position = suMsg.players[i].cubePos;
                }
                Debug.Log("Server update message received!");
            break;
            default:
            Debug.Log("Unrecognized message received!");
            break;
        }
    }

    void Disconnect(){
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect(){
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
    }   
    void Update()
    {


        if (Input.GetKey(KeyCode.DownArrow))
        {
            m_player.cubePos = m_player.cubePos + new Vector3(0.0f, -1.0f * Time.deltaTime, 0.0f);
        }
        if(Input.GetKey(KeyCode.UpArrow))
        {
            m_player.cubePos = m_player.cubePos + new Vector3(0.0f, 1.0f * Time.deltaTime, 0.0f);
        }

        if (!m_Connection.IsCreated)
        {
            return;
        }

        m_Driver.ScheduleUpdate().Complete();

        DataStreamReader stream;
        NetworkEvent.Type cmd;
        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                OnConnect();
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                OnData(stream);
            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                OnDisconnect();
            }

            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }
    }

    void sendHeatBeat()
    {
        print("sent Update");
        PlayerUpdateMsg m = new PlayerUpdateMsg();
        m.player = m_player;
        SendToServer(JsonUtility.ToJson(m));
    }

    void spawnCubes(int count)
    {
        for(int i = m_Players.Count; i<count; i++)
        {
            m_Players.Add(Instantiate(playerObject, new Vector3(0, 0, 0), Quaternion.identity));
        }
        for (int i = count; m_Players.Count > count; i--)
        {
            Destroy(m_Players[i]);
            m_Players.RemoveAt(i);
        }
    }

}