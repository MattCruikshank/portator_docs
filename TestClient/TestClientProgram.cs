namespace Portator;

using System;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("Hello, World!");
        Contact contact = new Contact
        {
            Name = "Matt",
            TailscaleAddress = "localhost:49994"
        };
        Message message = new Message
        {
            From = "Matt",
            To = "Tom",
            Body = { "First line", "Second line!" }
        };
        Client.UnreliablySendMessage(contact, message);
    }
}
