using Netimobiledevice.Exceptions;
using System;
using System.Net;
using System.Net.Sockets;

namespace Netimobiledevice.Usbmuxd
{
    /// <summary>
    /// Creates a socket connection to usbmuxd and handles the sending/recieving of packets. 
    /// For Max/Linux this is a unix domain socket, and for Windows it is a TCP socket. 
    /// </summary>
    internal class UsbmuxdSocket
    {
        private const int USBMUXD_SOCKET_PORT = 27015;
        private const string USBMUXD_SOCKET_FILE = "/var/run/usbmuxd";
        private const string USBMUXD_SOCKET_IP = "127.0.0.1";

        private readonly Socket socket;

        internal UsbmuxdSocket(Socket connectedSocket)
        {
            socket = connectedSocket ?? throw new ArgumentNullException(nameof(connectedSocket));
        }

        public UsbmuxdSocket(string usbmuxAddress = "")
        {
            try {
                EndPoint endpoint;
                AddressFamily family;
                if (!string.IsNullOrEmpty(usbmuxAddress)) {
                    if (usbmuxAddress.Contains(':')) {
                        // Assume a TCP address
                        string hostname = usbmuxAddress.Split(":")[0];
                        string port = usbmuxAddress.Split(":")[1];
                        endpoint = new IPEndPoint(IPAddress.Parse(hostname), Convert.ToUInt16(port));
                        family = AddressFamily.InterNetwork;
                    }
                    else {
                        // Assume a Unix domain address
                        endpoint = new UnixDomainSocketEndPoint(usbmuxAddress);
                        family = AddressFamily.Unix;
                    }
                }
                else {
                    if (OperatingSystem.IsWindows()) {
                        endpoint = new IPEndPoint(IPAddress.Parse(USBMUXD_SOCKET_IP), USBMUXD_SOCKET_PORT);
                        family = AddressFamily.InterNetwork;
                    }
                    else {
                        endpoint = new UnixDomainSocketEndPoint(USBMUXD_SOCKET_FILE);
                        family = AddressFamily.Unix;
                    }
                }

                socket = new Socket(family, SocketType.Stream, ProtocolType.IP);
                socket.Connect(endpoint);
            }
            catch (SocketException ex) {
                throw new UsbmuxConnectionException("Can't create and/or connect to Usbmux socket", ex);
            }
        }

        public void Close()
        {
            socket.Close();
        }

        public Socket GetInternalSocket()
        {
            return socket;
        }

        public byte[] Receive(int size)
        {
            if (size < 0) {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            byte[] buf = new byte[size];
            int received = 0;
            try {
                while (received < size) {
                    int count = socket.Receive(buf, received, size - received, SocketFlags.None);
                    if (count == 0) {
                        throw new UsbmuxException($"Usbmux socket closed after {received} of {size} bytes");
                    }
                    received += count;
                }
            }
            catch (SocketException ex) {
                throw new UsbmuxException($"Failed to receive {size} usbmux bytes (received {received})", ex);
            }
            return buf;
        }

        public int Send(byte[] message)
        {
            ArgumentNullException.ThrowIfNull(message);

            int sent = 0;
            try {
                while (sent < message.Length) {
                    int count = socket.Send(message, sent, message.Length - sent, SocketFlags.None);
                    if (count == 0) {
                        throw new UsbmuxException($"Usbmux socket closed after {sent} of {message.Length} bytes");
                    }
                    sent += count;
                }
            }
            catch (SocketException ex) {
                throw new UsbmuxException($"Failed to send {message.Length} usbmux bytes (sent {sent})", ex);
            }
            return sent;
        }

        public void SetBlocking(bool blocking)
        {
            socket.Blocking = blocking;
        }

        public void SetTimeout(int timeout)
        {
            socket.ReceiveTimeout = timeout;
            socket.SendTimeout = timeout;
        }
    }
}
