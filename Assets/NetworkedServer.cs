using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;
    LinkedList<UserInfo> UserInfos;
    int playerWaitingID = -1;
    int playerWaitingID2 = -1;
    string playerWaitingIDN = "";
    string playerWaitingIDN2 = "";
    string playerWaitingN = "";
    LinkedList<Chatroom> Chatrooms;
    int playerWaiting = -1;
    static int flag = 0;

    // Start is called before the first frame update
    void Start()
    {
        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);
        UserInfos = new LinkedList<UserInfo>();
        //read in player accounts
        LoadPlayerManagementFile();
        Chatrooms = new LinkedList<Chatroom>();
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
            foreach (UserInfo pa in UserInfos)
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
                UserInfo playerAccount = new UserInfo(id, n, p);
                UserInfos.AddLast(playerAccount);
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
            foreach (UserInfo pa in UserInfos)
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
        else if (signifier == ClientToServerSignifiers.JoinGameRoomQueue)
        {
            if (playerWaitingID == -1)
            {
                playerWaitingID = id;
                if (csv.Length > 1)
                {
                    playerWaitingN = csv[1];
                    AppendLogFile(csv[1] + ":Player has joined the game room to play from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);
                }
                else
                {
                    AppendLogFile("Player has joined the game room to play from connection " + id);
                    SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id, id);// msg format: signifier, clientid
                }
            }
            else
            {
                Debug.Log("Creating room");
                Chatroom gr = new Chatroom();
                gr.Player1 = new UserInfo(playerWaiting, playerWaitingN, "");
                gr.Player2 = new UserInfo(id, csv[1], "");
                Chatrooms.AddLast(gr);
                AppendLogFile(csv[1] + ":player join in game room for tick tac toe from connection " + id);
                SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], id);//msg format:signifier, clientid, joined player name
                SendMessageToClient(ServerToClientSignifiers.JoinedPlay + "," + id + "," + csv[1], playerWaiting);
                //sending notification about game start
                SendMessageToClient(ServerToClientSignifiers.DGameStart + ",1," + gr.Player2.name, gr.Player1.ID);//msg format:signifier, player number of yours, opponent player name
                SendMessageToClient(ServerToClientSignifiers.DGameStart + ",2," + gr.Player1.name, id);//msg format:signifier, player number of yours, opponent player name
                                                                                                       //reset flag for next game room
                playerWaiting = -1;
                playerWaitingN = "";
            }
        }
        else if (signifier == ClientToServerSignifiers.PlayGame)
        {
            Chatroom gr = GetChatroomClientId(id);
            if (gr != null)
            {
                if (gr.Player1.ID == id)
                {
                    SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + gr.Player1.name + "," + csv[2] + "," + csv[3] + "," + flag, gr.Player2.ID);
                    SendMessageToClient(ServerToClientSignifiers.SelfPlay + "," + gr.Player1.name + "," + csv[2] + "," + csv[3] + "," + flag, id);
                }
                else SendMessageToClient(ServerToClientSignifiers.OpponentPlay + "," + gr.Player1.ID, id);
            }
        }
        else if (signifier == ClientToServerSignifiers.SendMsg)
        {
            Debug.Log("send from s: " + msg);
            Chatroom gr = GetChatroomClientId(id);
            if (gr != null)
            {
                //if(gr.playerId1==id)
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player1.ID);
                //else if(gr.playerId2==id)
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player2.ID);
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], gr.Player3.ID);

                foreach (UserInfo ob in gr.ObserverList)
                {
                    SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + id + "," + csv[1] + "," + csv[2], ob.ID);
                }
            }
        }
        else if (signifier == ClientToServerSignifiers.SendPrefixMsg)
        {
            Debug.Log("send pr from s: " + msg);
            Chatroom gr = GetChatroomClientId(id);
            if (gr != null)
            {
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], gr.Player1.ID);
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], gr.Player2.ID);
                SendMessageToClient(ServerToClientSignifiers.ReceiveMsg + "," + csv[1], gr.Player3.ID);
            }
        }
        else if (signifier == ClientToServerSignifiers.JoinAsObserver)
        {
            Debug.Log("joined as observer");

            foreach (Chatroom gr in Chatrooms)
            {
                gr.addObserver(id, csv[1]);
                SendMessageToClient(ServerToClientSignifiers.someoneHasJoinedAsObserver + "," + id + "," + csv[1], gr.Player1.ID);
                SendMessageToClient(ServerToClientSignifiers.someoneHasJoinedAsObserver + "," + id + "," + csv[1], gr.Player2.ID);
                SendMessageToClient(ServerToClientSignifiers.someoneHasJoinedAsObserver + "," + id + "," + csv[1], gr.Player3.ID);
            }

        }

    }
    public void SavePlayerManagementFile()
    {
        StreamWriter sw = new StreamWriter(Application.dataPath + Path.DirectorySeparatorChar + "PlayerManagementFile.txt");
        foreach (UserInfo pa in UserInfos)
        {
            sw.WriteLine(UserInfo.PlayerIdSinifier + "," + pa.name + "," + pa.password);
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
                if (signifier == UserInfo.PlayerIdSinifier)
                {
                    UserInfos.AddLast(new UserInfo(int.Parse(csv[1]), csv[2], csv[3]));
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
    public Chatroom GetChatroomClientId(int playerId)
    {
        foreach (Chatroom gr in Chatrooms)
        {
            if (gr.Player1.ID == playerId || gr.Player2.ID == playerId || gr.Player3.ID == playerId)
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
    public const int JoinGameRoomQueue = 3;
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
    public const int ChatStart = 6;
    public const int ReceiveMsg = 7;
    public const int someoneHasJoinedAsObserver = 8;
    public const int ListOfPlayer = 8;
    public const int JoinedPlay = 9;
    public const int SelfPlay = 10;
    public const int DGameStart = 11;
}
public class UserInfo
{
    public const int PlayerIdSinifier = 1;
    public string name, password;
    public int ID;
    public UserInfo(int i, string n, string p)
    {
        ID = i;
        name = n;
        password = p;
    }

}
public class Chatroom
{
    public UserInfo Player1, Player2, Player3;
    public List<UserInfo> ObserverList;
    public Chatroom()
    {
            ObserverList = new List<UserInfo>();
    }

    public void addObserver(int ID, string n)
    {
        if (!ObserverList.Contains(new UserInfo(ID, n, "")))
             ObserverList.Add(new UserInfo(ID, n, ""));
    }

    public string getChatters()
    {
            string p = "";
            p += "," + Player1.ID + ":" + Player1.name;
            p += "," + Player2.ID + ":" + Player2.name;
            p += "," + Player3.ID + ":" + Player3.name;
            return p;
    }
    
}
