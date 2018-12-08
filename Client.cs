// Evan Greavu
// egreavu@asu.edu
// Client->Server Chat application 
// CLIENT

using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;

namespace ClientServerApp
{
    // Class for Main 
    class Program
    {
        static void Main(string[] args)
        {
            Client client = new Client(); // Instantiate a Client class, which contains the behavior of the client program
            client.StartListeningThread(); // Start the parallel listening thread

            while (client.Active) // This is our keyboard input loop
            {
                string line = Console.ReadLine(); // Receive input from the keyboard. Blocks here
                client.InterpretInput(line); // Decide what to do with this message to be sent
            }
            Console.WriteLine("\nPress any key to close...\n");
            Console.ReadKey();
        }
    }

    // Main class for Client behavior
    class Client
    {
        TokenGenerator gen; // For generating message id's
        UdpClient udp; // Udp client object
        IPEndPoint server_point = new IPEndPoint(IPAddress.Parse(Server.SERVER_IP), Server.SERVER_PORT);  // The messages will be sent to the server at this IP and port, default this machine port 3737
        string myToken; // For storing my token
        public bool Active; // For if the client loop is running.

        public Client()
        {
            udp = new UdpClient(); // Instantiate the Udp client
            udp.Connect(server_point); // Connect it to the point we are going to be sending messages to
            gen = new TokenGenerator();  // Create a TokenGenerator custom class that we can use to generate the message IDs
            myToken = string.Empty;
            Active = false;
        }

        // Decide what to do based on an input typed into the console
        public void InterpretInput(string input)
        {
            int index_arrow = input.IndexOf("->"); // Destination comes after if found
            int index_hashtag = input.IndexOf('#'); // Message comes after if found
            int index_bracket = input.IndexOf('<'); // Password comes after if found
            int index_logoff = input.IndexOf("logoff"); // logoff keyword is found
            int index_login = input.IndexOf("login"); // login keyword is found

            if (index_login == -1 && index_logoff == -1 && index_arrow != -1 && index_hashtag != -1) // This appears to be a message send. No login, no logoff. But a hashtag and arrow.
                SendToServer(AppendTokenAndId(input)); // Send the message with appended token and id.
            else
                SendToServer(input); // This does not appear to be message send. Do not append the token and id.
        }

        // Send a message to the server
        public void SendToServer(string message)
        {
            byte[] datagram = Encoding.ASCII.GetBytes(message);
            udp.Send(datagram, datagram.Count());
            //Console.WriteLine("Log: Sent {0} bytes:\n{1}", datagram.Count(), message);
        }

        // Start a thread to listen for messages from the server. All mesages are echoed to the console, and our token is updated to match what the server has given us. 
        public void StartListeningThread() // Listen on a loop for data from the server, so that we can get messages from other users.
        {
            Active = true;
            Thread t = new Thread(() => // Create a thread
            { //  This thread is a loop that receives messages.
                while (Active)
                {
                    IPEndPoint from_ip = new IPEndPoint(IPAddress.Any, 0); // Create an IPEndPoint for storing the IP and port of whoever sent us data (it's the server, it will be 127.0.0.1:3737)
                    byte[] received = new byte[1024]; // Create a 1KB buffer

                    try // It is possible that the server could not be found. We will catch this exception if it happens and exit the loop.
                    {
                        received = udp.Receive(ref from_ip); // Receive bytes into this buffer.
                        string message = Encoding.ASCII.GetString(received); // Decode the bytes into an ASCII string

                        //Console.WriteLine("Log: Received {0} bytes from server, message: {1}", received.Count(), message); // for debug
                        Console.WriteLine(message); // Echo the message to the console
                        FindTokenFromMessage(message); // Update our token
                    }
                    catch (SocketException ex) // SocketException will happen if the connection to the server could not be made for some reason
                    {
                        Console.WriteLine("\n\nSocket Exception: {0}", ex.Message);
                        Console.WriteLine("\nThe server likely could not be found. Make sure it is not blocked by firewall. It is looking for 127.0.0.1:3737 UDP.\n");
                        Active = false;
                    }
                    catch (Exception ex) // Any other exceptions
                    {
                        Console.WriteLine("\n\nUnknown exception has occurred: {0}\n{1}", ex.Message, ex.Source);
                        Console.WriteLine("\nI am not sure when this should ever happen. My apologies. -Evan G\n");
                        Active = false;
                    }

                }
            });

            t.Start(); // Start the thread.
        }

        // Extract and update our locally stored token out of a message from the server
        void FindTokenFromMessage(string message) // Every time a message is received, we update our token to match what the server says it is. We trust the server to be delivering the correct token.
        {
            int index_bracket = message.IndexOf('<'); // The first occurrence of a bracket in the message will be the beginning of the token
            if (index_bracket != -1 && message.Length > index_bracket + 6) // If the bracket is there and there is at least 6 characters for a substring (prevent crashing)
            {
                myToken = message.Substring(index_bracket + 1, 6); // the token is after the bracket and 6 long.
            }
        }

        // Format the message to be sent to the server with our token and a message id
        string AppendTokenAndId(string message)
        {
            int index_arrow = message.IndexOf("->");
            int index_hashtag = message.IndexOf('#');
            if (index_hashtag != -1 && index_arrow != -1)
            {
                string sent_username = message.Substring(0, index_arrow); // The username is before the arrow
                string sent_dest = message.Substring(index_arrow + 2, index_hashtag - index_arrow - 2); // the destination is after the arrow before hashtag
                string sent_message = message.Substring(index_hashtag + 1); // The message itself is after hashtag
                string messageid = gen.GenerateMessageId().ToString(); // Generate a message id 

                return string.Format("{0}->{1}#<{2}><{3}>{4}", sent_username, sent_dest, myToken, messageid, sent_message); // Return it all together
            }
            else
                return message; // If something was missing, do not modify the message, let it error out from the server.
        }

    }
}
