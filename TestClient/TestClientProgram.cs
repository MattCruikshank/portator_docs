namespace Portator;

using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("TestClientProgram");
        Message message = new Message
        {
            From = Client.GetSelfContact(),
            To = Client.GetAllContacts().First(),
            Body = { "First line", "Second line!" }
        };
        Client.UnreliablySendMessage(message);
    }
}
