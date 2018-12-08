// Evan Greavu
// egreavu@asu.edu
// Client->Server Chat application
// SERVER

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;

namespace ClientServerApp
{
    // Class for Main
    class Program
    {
        static void Main(string[] args)
        {
            Server server = new Server(); // Instantiate the server class which contains our definition for how the server runs.
            Console.WriteLine("Starting server.");

            server.StartListening();

            // After execution (should never happen, only on crash)
            Console.WriteLine("Press any key to close...");
            Console.ReadLine(); // Prevent this window from closing after the server is done executing, which should never happen.
        }
    }
    
    // Struct for holding an ip address and port together with the corresponding token and username, and how long this client has been connected.
    class ClientObject
    {
        public IPEndPoint ip;
        public string token;
        public string username;
        public int minutesConnected;

        public ClientObject(IPEndPoint ip, string t, string u)
        {
            this.ip = ip;
            token = t;
            username = u;
            minutesConnected = 0;
        }
    }

    // The Server object 
    public class Server
    {
        // Server Settings
        public const string SERVER_IP = "127.0.0.1";
        public const int SERVER_PORT = 3737;

        const int CONNECTION_TIMEOUT = 5; // Number of minutes to wait before disconnecting an inactive client
        const string userFile = "users.txt";  // Plaintext stored usernames and passwords in file users.txt (must be in same dir as executable)
        bool canStart = true; // Loop control bool 

        private UdpClient udp; // UDP socket object for both sending and receiving 
        private IPEndPoint server_bind = new IPEndPoint(IPAddress.Any, SERVER_PORT); // Will bind to SERVER_PORT and receive from anywhere

        TokenGenerator gen;                   // Custom class that generates the 6 character tokens and 10 digit IDs
        Dictionary<string, string> users;     // For storing usernames and passwords
        List<ClientObject> clients;           // For storing client IPs, tokens, and ports

        public Server()
        {
            try
            {
                udp = new UdpClient(server_bind); // Init Udp client, bind to our arbitrary port 3737
                gen = new TokenGenerator(); // Init token generator 
                LoadUserInformation(); // Load users.txt
                clients = new List<ClientObject>(); // Instantiate empty list of clients 

                if (users.Count() <= 0) // If the users.txt was not found during LoadUserInformation() we will have no clients...
                    Console.WriteLine("Warning: users.txt not found. No user database to load from.");

                TrackSessionTimesThread(); // Start the thread for watching the times clients have been inactive
            }
            catch (SocketException ex)
            {
                Console.WriteLine("SocketException: {0}", ex.Message);
                Console.WriteLine("The server could not bind to the port, probably because there is a program also using port 3737.");
                canStart = false;
            }

        }

        // Called when a client is connected for the first time
        ClientObject NewClient(string username, IPEndPoint p)
        {
            ClientObject client = new ClientObject(p, gen.GenerateToken(), username); // Create the data structure that holds the ip, port, token, and username
            clients.Add(client);
            return client;
        }

        // Remove a client object from our list according to a given IP address 
        void RemoveClient(IPEndPoint p) // Remove a client IP, port, and token from our stored list in the event of disconnect
        {
            int index = FindClient(p); // find the client with this IP and port
            if (index == -1) // If not found,...
            {
                Console.WriteLine("Error: Not found. Client could not be removed from clients list with ip {0}", p.Address.ToString());
                return; // do nothing
            }
            Console.WriteLine("Disconnected client {0}", clients[index].token);
            clients.RemoveAt(index); // Otherwise remove it
        }

        // Overload of above
        // Remove a client from our list of connected clients
        void RemoveClient(ClientObject client)
        {
            Console.WriteLine("Disconnected client with username {0}, token {1}", client.username, client.token);
            clients.Remove(client);
        }

        // Returns the index of the stored client with the given username, -1 if not found
        int FindClient(string username)
        {
            for (int i = 0; i < clients.Count(); i++)
            {
                if (clients[i].username.Equals(username))
                    return i;
            }
            return -1;
        }

        // Overload of above 
        // Returns the index of the stored client with the given IP and port, -1 if not found
        int FindClient(IPEndPoint p)
        {
            for (int i = 0; i < clients.Count(); i++)
            {
                if (clients[i].ip.Address.ToString().Equals(p.Address.ToString()) && clients[i].ip.Port.ToString().Equals(p.Port.ToString())) // Ugly string compare of IP and port
                    return i;
            }
            return -1;
        }

        // Send a message to the client at IP and port object p, verbatim 
        void SendToClient(IPEndPoint p, string message)
        {
            byte[] datagram = Encoding.ASCII.GetBytes(message); // Encode the string to bytes according to ASCII 
            udp.Send(datagram, datagram.Count(), p); // Send this datagram to the IPEndPoint
        }

        // Main logic method for the server
        public int StartListening()
        {
            if (! canStart)
            {
                Console.WriteLine("The server is unable to start.");
                return -1;
            }
            byte[] buffer = new byte[1024]; // 1KB buffer
            IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);  // Create blank IPEndPoint for storing the IP of senders

            while (true) // loop for receieving messages
            {
                try // Try statement for catching if the very next line fails, which will happen if a client prematurely disconnected and the server does not know, and is trying to send to this IP and port.
                {
                    buffer = udp.Receive(ref sender); // When data arrives, put it in the buffer. Also set the 'sender' IPEndPoint object equal to the client who sent the message
                    // Will throw SocketException if not possible.

                    //Console.WriteLine("Received message from {0}", sender.Address.ToString()); // for debug

                    int clientnum = FindClient(sender); // Take the sender's IPEndPoint object and attempt to find a connected client with this information

                    string msg = Encoding.ASCII.GetString(buffer); // Decode the message from bytes into an ASCII string
                                                                   //Console.WriteLine(msg);

                    // Finding essential parts of the message
                    int index_login = msg.IndexOf("login"); // Find the word "login" in the message. If it is found, the index_login is not equal to -1.
                    int index_logoff = msg.IndexOf("logoff"); // Find the word "logoff" in the message

                    int index_arrow = msg.IndexOf("->"); // The arrow in the command comes after the username and before the target username
                    int index_hashtag = msg.IndexOf('#'); // The # in the command comes before the message
                    int index_firstbracket = msg.IndexOf('<'); // The first < in the command comes before the token
                    int index_lastbracket = msg.LastIndexOf('<'); // The last < in the command comes before the password or messageid


                    //////////////////////////////////////////////////////////////////////////////////////////////////////////////
                    // Main Server Logic

                    if (index_login != -1) // The word 'login' was found. This is a login attempt. 
                    {
                        if (index_arrow != -1 && index_hashtag != -1 && index_lastbracket != -1) // If the appropriate characters for a login command are all found... e.g. evan->server#login<hello23>
                        {
                            string sent_username = msg.Substring(0, index_arrow); // The username comes first, and ends at the beginning of the arrow
                            string sent_destination = msg.Substring(index_arrow + 2, index_hashtag - index_arrow - 2); // The destination comes after the arrow and before hashtag
                            string sent_password = msg.Substring(index_lastbracket + 1).TrimEnd('>'); // The password will come after the left bracket, also trim off the last right bracket. 

                            if (UserIsCorrect(sent_username, sent_password)) // Check if the client sent the correct username and password
                            {
                                if (sent_destination.Equals("server")) // Make sure they are trying to login to the server and not something else.
                                {
                                    Console.WriteLine("Succesfully authenticated user {0}", sent_username);
                                    if (clientnum == -1) // if the client was not yet found in the list of connected clients...
                                    {
                                        ClientObject client = NewClient(sent_username, sender); // Create the client object and store in list of connected clients
                                        SendToClient(sender, string.Format("server->{0}#Success<{1}>", client.username, client.token)); // and let them know of success.
                                    }
                                    else // The client is already connected, let them know.
                                    {
                                        ClientObject client = clients[clientnum];
                                        SendToClient(sender, string.Format("server->{0}#<{1}>Error: already logged in", client.username, client.token));
                                    }
                                }
                                else
                                {
                                    SendToClient(sender, string.Format("server->{0}#Error: Invalid destination \"{1}\"", sent_username, sent_destination));
                                }
                            }
                            else // Incorrect password or username
                            {
                                SendToClient(sender, string.Format("server->{0}#Error: Password does not match!", sent_username));
                            }
                        }
                        else
                        {
                            SendToClient(sender, string.Format("server->client#Error: Please enter a username and password!")); // Failed to supply a username or password when logging in
                        }
                    }

                    else if (index_logoff != -1) // The word logoff was found. This is a logoff attempt.
                    {
                        if (index_arrow != -1 && index_hashtag != -1) // e.g. evan->server#logoff contains an arrow and a hashtag, but not left bracket
                        {
                            string sent_username = msg.Substring(0, index_arrow); // The username comes first, and ends at the beginning of the arrow
                            string sent_destination = msg.Substring(index_arrow + 2, index_hashtag - index_arrow - 2); // The destination comes after the arrow and before hashtag

                            if (sent_destination.Equals("server")) // Correctly formmated logoff command: evan->server#logoff
                            {
                                if (clientnum != -1) // The client is indeed connected
                                {
                                    SendToClient(sender, string.Format("server->{0}#Success<{1}>", clients[clientnum].username, clients[clientnum].token));
                                    RemoveClient(sender); // Remove them from the list of connected clients. Note: this must happen after the message is sent or else we will not know what their token is.
                                }
                                else // The client is not connected yet and they tried to disconnect
                                {
                                    SendToClient(sender, string.Format("server->{0}#Error: Not logged in", sent_username));
                                }
                            }
                            else // The destination of this logoff attempt is not the server, which is invalid
                            {
                                SendToClient(sender, string.Format("server->{0}#Error: Invalid destination \"{1}\"", sent_username, sent_destination));
                            }
                        }
                        else // Incorrectly formatted logoff command
                        {
                            if (clientnum != -1) // If we can tell that they are a connected client who just messed up, refer to them by username 
                                SendToClient(sender, string.Format("server->{0}#Error: Incorrectly formatted command", clients[clientnum].username));
                            else // if they are not even connected and they tried logoff AND messed it up then just call them client
                                SendToClient(sender, string.Format("server->#clientError: Incorrectly formatted command"));
                        }
                    }

                    // This is a message attempt.
                    // e.g. evan->ethan#<XYBVSX><1301233542>hello there
                    else if (index_hashtag != -1 && index_arrow != -1 && index_firstbracket != -1 && index_lastbracket != -1) // This contains everything necessary for a message: ->, #, and 2 <'s
                    {
                        string sent_username = msg.Substring(0, index_arrow); // The username comes first, and ends at the beginning of the arrow
                        string sent_destination = msg.Substring(index_arrow + 2, index_hashtag - index_arrow - 2); // The destination comes after the arrow and before hashtag
                        string sent_token = msg.Substring(index_firstbracket + 1, 6); // The sent token comes after the first left bracket and is 6 long
                        string sent_messageid = msg.Substring(index_lastbracket + 1, 10); // The messageid comes after the last left bracket and is 10 long
                        string sent_message = msg.Substring(index_lastbracket + 12); // The message to be sent comes after the messageid, which ends 12 after the index of last left bracket.


                        if (clientnum != -1) // The client who sent this message is connected.
                        {
                            ClientObject client = clients[clientnum]; // Get the client object for who sent the message from our list
                            client.minutesConnected = 0; // Now we know they are active, so reset their inactive time.

                            if (sent_username.Equals(client.username)) // Make sure the username of this command is correct
                            {
                                if (sent_token.Equals(client.token)) // Make sure the token of this command is correct for the user.
                                {
                                    // Attempt to find destination client in list of connected clients
                                    int destnum = FindClient(sent_destination);
                                    if (destnum != -1) // The destination client is found which means they are connected and the message can be sent.
                                    {
                                        ClientObject dest = clients[destnum]; // Get the destination object stored in our list

                                        // Message sent to dest will look like evan->ethan#<10G3Kj><1301233542>hello there
                                        SendToClient(dest.ip, string.Format("{0}->{1}#<{2}><{3}>{4}", client.username, dest.username, dest.token, sent_messageid, sent_message)); // Forward to destination, use their token.

                                        SendToClient(sender, string.Format("server->{0}#<{1}><{2}>Success: {3}", client.username, client.token, sent_messageid, sent_message)); // also tell client that it was successful.
                                    }
                                    else // The destination client was not found. 
                                    {
                                        SendToClient(sender, string.Format("server->{0}#<{1}><{2}>Error: destination offline!", client.username, client.token, sent_messageid));
                                    }
                                }
                                else // Incorrect token
                                {
                                    SendToClient(sender, string.Format("server->{0}#<{1}><{2}>Error: token error!", client.username, client.token, sent_messageid));
                                }
                            }
                            else // Username error
                            {
                                SendToClient(sender, string.Format("server->{0}#<{1}><{2}>Error: username error!", client.username, client.token, sent_messageid));
                            }
                        }
                        else // Sender is not found in clients list, so not logged in
                        {
                            SendToClient(sender, string.Format("server->{0}#<{1}>Error: Not logged in!", sent_username, sent_messageid));
                        }
                    }

                    else // We cannot tell what kind of command this is at all.
                    {
                        if (clientnum != -1) // If we can tell that they are connected,
                            SendToClient(sender, string.Format("server->{0}#Error: Incorrectly formatted command", clients[clientnum].username)); // refer to them as their username.
                        else
                            SendToClient(sender, string.Format("server->client#Error: Incorrectly formatted command")); // otherwise just call them "client".
                    }
                }
                catch (SocketException ex) // If a message could not be received from this specific IPEndPoint, an error has occurred. 
                {
                    int tryfindclient = FindClient(sender);
                    if (tryfindclient != -1)
                    {
                        Console.WriteLine("SocketException: {0}", ex.Message);
                        Console.WriteLine("Error when sending to client, may be prematurely disconnected");
                        SendToClient(sender, string.Format("server->{0}#<{1}>Error: destination offline!", clients[tryfindclient].username, clients[tryfindclient].token));
                    }
                }
            }
        }

        // In a parallel thread, increment the number of minutes each client object has been connected by 1. If any hit 5, disconnect them.
        private void TrackSessionTimesThread()
        {
            Thread t = new Thread(thread => // Create a new thread
            {
                while (true)
                {
                    Thread.Sleep(60000); // Sleep for 60,000 milliseconds, or 1 minute.

                    for (int i = 0; i < clients.Count(); i++)
                    {
                        if (clients[i].minutesConnected++ > CONNECTION_TIMEOUT)
                        {
                            SendToClient(clients[i].ip, string.Format("server->{0}#<{1}>Notify: Disconnecting inactive user", clients[i].username, clients[i].token)); // Le the client know they are being disconnected
                            RemoveClient(clients[i]);
                        }
                    }
                }
            });

            t.Start(); // Start the thread we created.
        }

        // Check if the username and password are correct
        private bool UserIsCorrect(string username, string password)
        {
            string correctPass;
            bool foundUser = users.TryGetValue(username, out correctPass);
            if (foundUser)
                return password.Equals(correctPass);
            else return false;
        }

        // Load the users.txt file into our dictionary data structure that ties usernames to passwords
        private void LoadUserInformation()
        {
            FileStream file = new FileStream(userFile, FileMode.OpenOrCreate); // Create a FileStream towards our file
            StreamReader reader = new StreamReader(file); // Create a StreamReader for reading this stream 1 line at a time

            Dictionary<string, string> result = new Dictionary<string, string>(); // Create an empty dictionary that will tie keys (usernames) to values (passwords)

            string line;
            while (!reader.EndOfStream)                         
            {                                                         // users.txt stores users per line like username|password
                line = reader.ReadLine();                             // For example line is evan|hello24
                int indexOfBar = line.IndexOf('|');                   // So the bar would be at index 4
                if (indexOfBar > 0) // if the bar is found...
                {
                    string name = line.Substring(0, indexOfBar);      // So the name is from 0 to 4
                    string pass = line.Substring(indexOfBar + 1);     // So the password is from 5 to the end
                    result.Add(name, pass);                           // Add this pair to the dictionary
                }
            }

            users = result; // Dictionary will be empty is file was not found or was empty
        }
    }

    // Custom class for generating the tokens and message id's
    public class TokenGenerator
    {
        private Random rand;

        public TokenGenerator()
        {
            rand = new Random();
        }

        // Create a 6-character unique token for clients
        public string GenerateToken()
        {
            string result = "";

            for (int i = 0; i < 6; i++)
                result += (char) rand.Next(63, 126); // Append 6 random ASCII characters to the string in a loop, between capital A and lowercase z

            return result;
        }

        // Create a 10 digit unique long integer for message IDs
        public long GenerateMessageId()
        {
            return (long)(1e9 + (rand.NextDouble() * (9e9 - 1))); // 1,000,000,000 to 9,999,999,999. rand.NextDouble() returns a double between 0 and 1. 
            // Return a number between 1000000000 and 9999999999
        }
    }
}
