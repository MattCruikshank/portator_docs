namespace Portator;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Sodium;

public class Client
{
    // public IEnumerable<Contact> GetAllContacts() {
    //     return Enumerable.Empty<Contact>();
    // }

    // If the recipient is online, they receive the message immediately.  Like UDP.
    public static void UnreliablySendMessage(Contact contact, Message message)
    {
        Console.WriteLine("UnreliablySendMessage");
        Console.WriteLine($"contact: {contact}");
        Console.WriteLine($"message: {message}");
    }

    // If the recipient is online, have a two-way conversation.
    // public void StreamMessages() {}

    // Try to send to the recipient now, if they're online; otherwise store the message for them to receive later.
    // public void ReliablySendMessage() {}

    // public void UnreliablyReceiveMessages() {}

    public static void TestMain()
    {
        // Generate a new keypair (Curve25519 key pair for crypto_box)
        var keyPair = PublicKeyBox.GenerateKeyPair();

        string publicKeyBase64 = Convert.ToBase64String(keyPair.PublicKey);
        string privateKeyBase64 = Convert.ToBase64String(keyPair.PrivateKey);

        Console.WriteLine("Public Key (Base64): " + publicKeyBase64);
        Console.WriteLine("Private Key (Base64): " + privateKeyBase64);

        // Message to encrypt
        string message = "Hello Portator using libsodium!";
        byte[] messageBytes = Encoding.UTF8.GetBytes(message);

        // Generate a random nonce (24 bytes)
        var nonce = PublicKeyBox.GenerateNonce();

        // Encrypt the message using recipient's public key and sender's private key
        byte[] encrypted = PublicKeyBox.Create(messageBytes, nonce, keyPair.PublicKey, keyPair.PrivateKey);

        Console.WriteLine("Encrypted (Base64): " + Convert.ToBase64String(encrypted));
        Console.WriteLine("Nonce (Base64): " + Convert.ToBase64String(nonce));

        // Decrypt the message using sender's public key and recipient's private key
        byte[] decrypted = PublicKeyBox.Open(encrypted, nonce, keyPair.PublicKey, keyPair.PrivateKey);

        string decryptedMessage = Encoding.UTF8.GetString(decrypted);
        Console.WriteLine("Decrypted Message: " + decryptedMessage);
    }
}