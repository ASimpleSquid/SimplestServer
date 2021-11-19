using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;
    LinkedList<PlayerAccount> playerAccounts;
    int playerWaitingID = -1;
    int playerWaitingID2 = -1;
    string playerWaitingIDN = "";
    string playerWaitingIDN2 = "";
    LinkedList<GameRoom> gameRooms;

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);
        playerAccounts = new LinkedList<PlayerAccount>();
        //read in player accounts
        LoadPlayerManagementFile();
        gameRooms = new LinkedList<GameRoom>();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }
  
    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }
    
    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);
        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);
        string n = "";
        string p = "";
        if (csv.Length > 2)
        {
            n = csv[1];
            p = csv[2];
        }
        bool nameIsInUse = false;
        bool validUser = false;
        if (signifier == ClientToServerSignifiers.CreateAccount)
        {
            Debug.Log("create account");
            //chk if player already exists
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n)
                {
                    nameIsInUse = true;
                    break;
                }
            }
            if (nameIsInUse)
            {
                SendMessageToClient(ServerToClientSignifiers.AccountCreationFailed + "", id);
                AppendLogFile(ServerToClientSignifiers.AccountCreationFailed + "," + System.DateTime.Now.ToString("HH:mm:ss MM/dd/yyyy"));
            }
            else
            {
                ///if not create new, add to list
                PlayerAccount playerAccount = new PlayerAccount(n, p);
                playerAccounts.AddLast(playerAccount);
                //send to client suc or fail
                SendMessageToClient(ServerToClientSignifiers.AccountCreationComplete + "", id);
                // save list to hd
                SavePlayerManagementFile();
            }


        }
        else if (signifier == ClientToServerSignifiers.Login)
        {
            Debug.Log("login");

            //chk if player is already exists,
            foreach (PlayerAccount pa in playerAccounts)
            {
                if (pa.name == n && pa.password == p)
                {
                    validUser = true;
                    break;
                }
            }
            //send to client suc or fail
            if (validUser)
                SendMessageToClient(ServerToClientSignifiers.LoginComplete + "," + n, id);
            else
                SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", id);
        }
        else if (signifier == ClientToServerSignifiers.JoinGammeRoomQueue)
        {
            if (playerWaitingID == -1)
            {
                playerWaitingID = id;
                playerWaitingIDN = n;
                // SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "", id);
            }
            if (playerWaitingID2 == -1)
            {
                playerWaitingID2 = id;
                playerWaitingIDN2 = n;
                // SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "", id);
            }
            else
            {
                GameRoom gr = new GameRoom(playerWaitingID, playerWaitingIDN, playerWaitingID2, playerWaitingIDN2, id, n);
                gameRooms.AddLast(gr);

                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + playerWaitingIDN2 + ":" + playerWaitingID2 + "," + n + ":" + id, gr.playerId1);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + playerWaitingIDN + ":" + playerWaitingID + "," + n + ":" + id, gr.playerId2);
                SendMessageToClient(ServerToClientSignifiers.GameStart + "," + playerWaitingIDN + ":" + playerWaitingID + "," + playerWaitingIDN2 + ":" + playerWaitingID2, gr.playerID3);
                playerWaitingID = -1;
                playerWaitingID2 = -1;
                playerWaitingIDN = "";
                playerWaitingIDN2 = "";
            }
        }
        else if (signifier == ClientToServerSignifiers.PlayGame)
        {
            GameRoom gr = GetGameRoomClientId(id);
            if (gr != null)
            {
                if (gr.playerId1 == id)
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + gr.playerId2, id);
                else SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + gr.playerId1, id);
            }
        }
        else if (signifier == ClientToServerSignifiers.SendMsg)
        {
            Debug.Log("send from s: " + msg);
            GameRoom gr = GetGameRoomClientId(id);
            if (gr != null)
            {
                //if(gr.playerId1==id)
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], gr.playerId2);
                //else if(gr.playerId2==id)
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], gr.playerId1);
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], gr.playerID3);
                foreach (int ob in gr.ObserverList)
                {
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], ob);
                }
            }
        }
        else if (signifier == ClientToServerSignifiers.SendPrefixMsg)
        {
            Debug.Log("send pr from s: " + msg);
            GameRoom gr = GetGameRoomClientId(id);
            if (gr != null)
            {
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], gr.playerId1);
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], gr.playerId2);
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], gr.playerID3);
            }
        }
        else if (signifier == ClientToServerSignifiers.JoinAsObserver)
        {
            Debug.Log("join as observer");
            foreach (GameRoom gr in gameRooms)
            {
                gr.ObserverList.Add(id);
                SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id, gr.playerId1);
                SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id, gr.playerId2);
                SendMessageToClient(ServerToClientSignifiers.someoneJoinedAsObserver + "," + id, gr.playerID3);
            }

        }

    }
    public void SavePlayerManagementFile()
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt");
        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(PlayerAccount.PlayerIdSinifier + "," + pa.name + "," + pa.password);
        }
        sw.Close();
    }

    public void LoadPlayerManagementFile()
    {
        if (File.Exists(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt"))
        {
            StreamReader sr = new StreamReader(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt");
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                int signifier = int.Parse(csv[0]);
                if (signifier == PlayerAccount.PlayerIdSinifier)
                {
                    playerAccounts.AddLast(new PlayerAccount(csv[1], csv[2]));
                }
            }
        }
    }
    public void AppendLogFile(string line)
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt", true);

        sw.WriteLine(line);

        sw.Close();
    }

    public void LoadLogFile()
    {
        if (File.Exists(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt"))
        {
            StreamReader sr = new StreamReader(Application.dataPath + Path.DirectorySeparatorChar + "Log.txt");
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                int signifier = int.Parse(csv[0]);

            }
        }
    }
    public GameRoom GetGameRoomClientId(int playerId)
    {
        foreach (GameRoom gr in gameRooms)
        {
            if (gr.playerId1 == playerId || gr.playerId2 == playerId || gr.playerID3 == playerId)
            {
                return gr;
            }
        }
        return null;
    }

}
public static class ClientToServerSignifiers
{
    public const int CreateAccount = 1;
    public const int Login = 2;
    public const int JoinGammeRoomQueue = 3;
    public const int PlayGame = 4;
    public const int SendMsg = 5;
    public const int SendPrefixMsg = 6;
    public const int JoinAsObserver = 7;
}
public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int AccountCreationComplete = 3;
    public const int AccountCreationFailed = 4;
    public const int OpponentPlay = 5;
    public const int GameStart = 6;
    public const int ReceiveMsg = 7;
    public const int someoneJoinedAsObserver = 8;
    public const int ListOfPlayer = 8;
}
public class PlayerAccount
{
    public const int PlayerIdSinifier = 1;
    public string name, password;
    public PlayerAccount(string n, string p)
    {
        name = n;
        password = p;
    }

}
public class GameRoom
{
    public int playerId1, playerId2, playerID3;
    public string playerIdN, playerId2N, playerID3N;
    public List<int> ObserverList;
    public GameRoom(int P1, string n1, int P2, string n2, int P3, string n3)
    {
        playerId1 = P1;
        playerIdN = n1;
        playerId2 = P2;
        playerId2N = n2;
        playerID3N = n3;
        playerID3 = P3;
        ObserverList = new List<int>();
    }
}
