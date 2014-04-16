﻿using System;
using System.Net;
using System.Threading.Tasks;
using Akka.Actor;
using Google.ProtocolBuffers;
using Helios.Exceptions;
using Helios.Net;
using Helios.Ops;
using Helios.Topology;

namespace Akka.Remote.Transport.Helios
{
    /// <summary>
    /// INTERNAL API
    /// 
    /// Abstract base class for adapting Helios <see cref="IConnection"/> objects to work with Akka.Remote.Transports
    /// </summary>
    internal abstract class HeliosHelpers : IConnection
    {
        protected IConnection UnderlyingConnection;

        protected HeliosHelpers(IConnection underlyingConnection)
        {
            UnderlyingConnection = underlyingConnection;
            UnderlyingConnection.OnConnection += OnConnect;
            UnderlyingConnection.OnDisconnection += OnDisconnect;
            UnderlyingConnection.OnError += OnException;
            OnConnection += OnConnect;
            OnDisconnection += OnDisconnect;
            OnError += OnException;
        }

        /// <summary>
        /// Binds the events for any incoming TCP activity
        /// </summary>
        protected void BindEvents(IConnection underlyingConnection)
        {
            underlyingConnection.OnConnection += OnConnect;
            underlyingConnection.OnDisconnection += OnDisconnect;
            underlyingConnection.OnError += OnException;
            underlyingConnection.BeginReceive(OnMessage);
        }

        protected abstract void OnConnect(INode remoteAddress, IConnection responseChannel);
        protected abstract void OnDisconnect(HeliosConnectionException cause, IConnection closedChannel);

        protected abstract void OnMessage(NetworkData data, IConnection responseChannel);

        protected abstract void OnException(Exception ex, IConnection erroredChannel);

        #region Static Methods

        /// <summary>
        /// Converts a <see cref="ByteString"/> structure into a Helios <see cref="NetworkData"/> structure
        /// </summary>
        /// <param name="byteString">The data to send over the network</param>
        /// <param name="address">The address that we received data from / are sending data to</param>
        /// <returns>a new <see cref="NetworkData"/> struct</returns>
        public static NetworkData ToData(ByteString byteString, Address address)
        {
            var data = new NetworkData()
            {
                Buffer = byteString.ToByteArray(),
                RemoteHost = HeliosTransport.AddressToNode(address)
            };
            data.Length = data.Buffer.Length;
            return data;
        }

        /// <summary>
        /// Converts a <see cref="NetworkData"/> structure into a <see cref="ByteString"/>
        /// </summary>
        /// <param name="data">The data we received from the network</param>
        /// <returns>A populated <see cref="ByteString"/> instance</returns>
        public static ByteString FromData(NetworkData data)
        {
            return ByteString.CopyFrom(data.Buffer, 0, data.Length);
        }

        #endregion

        #region IConnection members

        public virtual void Dispose()
        {
            UnderlyingConnection.Dispose();
        }

        public bool IsOpen()
        {
            return UnderlyingConnection.IsOpen();
        }

        public virtual Task<bool> OpenAsync()
        {
            return UnderlyingConnection.OpenAsync();
        }

        public void Configure(IConnectionConfig config)
        {
            UnderlyingConnection.Configure(config);
        }

        public virtual void Open()
        {
            UnderlyingConnection.Open();
        }

        public void BeginReceive()
        {
            UnderlyingConnection.BeginReceive();
        }

        public void BeginReceive(ReceivedDataCallback callback)
        {
            UnderlyingConnection.BeginReceive(OnMessage);
        }

        public void StopReceive()
        {
            UnderlyingConnection.StopReceive();
        }

        public void Close()
        {
            UnderlyingConnection.Close();
        }

        public void Send(NetworkData payload)
        {
            UnderlyingConnection.Send(payload);
        }

        public Task SendAsync(NetworkData payload)
        {
            return UnderlyingConnection.SendAsync(payload);
        }

        public IEventLoop EventLoop { get { return UnderlyingConnection.EventLoop; } }
        public DateTimeOffset Created { get { return UnderlyingConnection.Created; } }
        public INode RemoteHost { get { return UnderlyingConnection.RemoteHost; } }
        public INode Local { get { return UnderlyingConnection.Local; } }
        public TimeSpan Timeout { get { return UnderlyingConnection.Timeout; } }
        public TransportType Transport { get { return UnderlyingConnection.Transport; } }
        public bool Blocking { get; set; }
        public bool WasDisposed { get { return UnderlyingConnection.WasDisposed; } }
        public bool Receiving { get { return UnderlyingConnection.Receiving; } }
        public int Available { get { return UnderlyingConnection.Available; } }

        public event ReceivedDataCallback Receive;
        public event ConnectionEstablishedCallback OnConnection;
        public event ConnectionTerminatedCallback OnDisconnection;
        public event ExceptionCallback OnError;

        #endregion
    }

    internal abstract class CommonHandlers : HeliosHelpers
    {
        protected HeliosTransport WrappedTransport;

        protected CommonHandlers(IConnection underlyingConnection) : base(underlyingConnection)
        {
        }

        public override void Open()
        {
            WrappedTransport.ConnectionGroup.TryAdd(this);
            base.Open();
        }

        public override Task<bool> OpenAsync()
        {
            WrappedTransport.ConnectionGroup.TryAdd(this);
            return base.OpenAsync();
        }

        protected abstract AssociationHandle CreateHandle(IConnection channel, Address localAddress,
            Address remoteAddress);

        protected abstract void RegisterListener(IConnection channel, IHandleEventListener listener, NetworkData msg,
            INode remoteAddress);

        protected void Init(IConnection channel, INode remoteSocketAddress, Address remoteAddress, NetworkData msg,
            out AssociationHandle op)
        {
            var localAddress = HeliosTransport.NodeToAddress(channel.Local, WrappedTransport.SchemeIdentifier,
                WrappedTransport.System.Name, WrappedTransport.Settings.Hostname);

            if (localAddress != null)
            {
                var handle = CreateHandle(channel, localAddress, remoteAddress);
                handle.ReadHandlerSource.Task.ContinueWith(s =>
                {
                    var listener = s.Result;
                    RegisterListener(channel, listener, msg, remoteSocketAddress);
                }, TaskContinuationOptions.AttachedToParent & TaskContinuationOptions.ExecuteSynchronously & TaskContinuationOptions.NotOnCanceled & TaskContinuationOptions.NotOnFaulted);
                op = handle;
            }
            else
            {
                op = null;
                channel.Close();
            }
        }

        public override void Dispose()
        {
            WrappedTransport.ConnectionGroup.TryRemove(this);
        }
    }
}
