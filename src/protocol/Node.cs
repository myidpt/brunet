/*
This program is part of BruNet, a library for the creation of efficient overlay
networks.
Copyright (C) 2005  University of California

This program is free software; you can redistribute it and/or
modify it under the terms of the GNU General Public License
as published by the Free Software Foundation; either version 2
of the License, or (at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program; if not, write to the Free Software
Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
*/

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Threading;

namespace Brunet
{

  /**
   * This class represents endpoint of packet communication.
   * There may be many nodes on each computer on the Brunet
   * Network.  An example may be :  a user wants to chat, and
   * share a file.  The file may be represented as a Node on
   * the Brunet Network, and also the chat user may represent
   * itself as a Node on the network.  Both of these Nodes
   * reside on the same computer host.
   *
   * The Node also keeps itself connected and manages its
   * connections. 
   * 
   */
  abstract public class Node : ISender, IDataHandler
  {
    /**
     * Create a node with a given local address and
     * a set of Routers.
     */
    public Node(Address addr)
    {
      //Start with the address hashcode:

      _sync = new Object();
      lock(_sync)
      {
        /*
         * Make all the hashtables : 
         */
        _local_add = AddressParser.Parse( addr.ToMemBlock() );
        _subscription_table = new Hashtable();

        _task_queue = new TaskQueue();
        //Here is the thread for announcing packets
        _packet_queue = new BlockingQueue();

        _running = false;
        _send_pings = true;
        _announce_thread = new Thread(this.AnnounceThread);

        _connection_table = new ConnectionTable(_local_add);
        _connection_table.ConnectionEvent += this.ConnectionHandler;

        _broadcastrpc = new BroadcastRPC();
        _codeinjection = new CodeInjection(this);
        _codeinjection.LoadLocalModules();
        /*
         * We must later make sure the EdgeEvent events from
         * any EdgeListeners are connected to _cph.EdgeHandler
         */
        /**
         * Here are the protocols that every edge must support
         */
        /* Here are the transport addresses */
        _remote_ta = new ArrayList();
        /*@throw ArgumentNullException if the list ( new ArrayList()) is null.
         */
        /* EdgeListener's */
        _edgelistener_list = new ArrayList();
        _edge_factory = new EdgeFactory();

        /* Initialize this at 15 seconds */
        _connection_timeout = new TimeSpan(0,0,0,0,15000);
        /* Set up the heartbeat */
        _heart_period = 500; //500 ms, or 1/2 second.
        _timer = new Timer(new TimerCallback(this.HeartBeatCallback),
                           null, _heart_period, _heart_period);
        //Check the edges from time to time
        this.HeartBeatEvent += new EventHandler(this.CheckEdgesCallback);
        _last_edge_check = DateTime.UtcNow;
      }
    }
    /**
     * This class represents the demultiplexing of each
     * type of data to different handlers
     */
    protected class NodeSource : ISource {
      protected volatile ArrayList _subs;
      protected readonly object _sync;
      protected class Sub {
        public readonly IDataHandler Handler;
        public readonly object State;
        public Sub(IDataHandler dh, object state) { Handler = dh; State = state; }
        public void Handle(MemBlock b, ISender retpath) {
          Handler.HandleData(b, retpath, State);
        }
        //So we can look up subscriptions based only on Handler equality
        public override bool Equals(object o) {
          Sub s = o as Sub;
          if( s != null ) {
            return (s.Handler == Handler);
          }
          else {
            return false;
          }
        }
        public override int GetHashCode() { return Handler.GetHashCode(); }
      }

      public NodeSource() {
        _subs = new ArrayList();
        _sync = new object();
      }

      public void Subscribe(IDataHandler h, object state) {
        Sub s = new Sub(h, state);
        //We have to lock so there is no race between the read and the write
        lock( _sync ) {
          _subs = Functional.Add(_subs, s);
        }
      }
      public void Unsubscribe(IDataHandler h) {
        Sub s = new Sub(h, null);
        int idx = _subs.IndexOf(s);
        //We have to lock so there is no race between the read and the write
        lock( _sync ) {
          _subs = Functional.RemoveAt(_subs, idx);
        }
      }
      /**
       * @return the number of Handlers that saw this data
       */
      public int Announce(MemBlock b, ISender return_path) {
        ArrayList subs = _subs;
        int handlers = subs.Count;
        for(int i = 0; i < handlers; i++) {
          Sub s = (Sub)subs[i];
          //No need to lock since subs can never change
          s.Handle(b, return_path);
        }
        return handlers;
      }
    }
    /**
     * Keeps track of the objects which need to be notified 
     * of certain packets.
     */
    protected Hashtable _subscription_table;

    protected readonly Address _local_add;
    /**
     * The Address of this Node
     */
    public Address Address
    {
      get
      {
        return _local_add;
      }
    }
    protected EdgeFactory _edge_factory;
    /**
     *  my EdgeFactory
     */
    public EdgeFactory EdgeFactory { get { return _edge_factory; } }

    /**
     * Here are all the EdgeListener objects for this Node
     */
    protected ArrayList _edgelistener_list;
    /**
     * These are all the local TransportAddress objects that
     * refer to EdgeListener objects attached to this node.
     * This IList is ReadOnly
     */
    public IList LocalTAs {
      get {
        //Make sure we don't keep too many of these things:
        ArrayList local_ta = new ArrayList();
        foreach(EdgeListener el in _edgelistener_list) {
          foreach(TransportAddress ta in el.LocalTAs) {
            local_ta.Add(ta);
            if( local_ta.Count >= _MAX_RECORDED_TAS ) {
              break;
            }
          }
          if( local_ta.Count >= _MAX_RECORDED_TAS ) {
            break;
          }
        }
        return local_ta;
      }
    }

    /**
     * This is an estimate of the current
     * network size.  It is not an exact
     * value.
     *
     * A value of -1 means there is not
     * enough information to make a meaningful
     * estimate.
     */
    virtual public int NetworkSize {
      get { return -1; }
    }
    protected BlockingQueue _packet_queue;

    protected object _heartbeat_sync = new object();
    protected bool _heartbeat_running = false;

    protected string _realm = "global";
    /**
     * Each Brunet Node is in exactly 1 realm.  This is 
     * a namespacing feature.  This allows you to create
     * Brunets which are separate from other Brunets.
     *
     * The default is "global" which is the standard
     * namespace.
     */
    public string Realm { get { return _realm; } }
    
    protected ArrayList _remote_ta;
    /**
     * These are all the remote TransportAddress objects that
     * this Node may use to connect to remote Nodes
     *
     * This can be shared between nodes or not.
     *
     * This is the ONLY proper way to set the RemoteTAs for this
     * node.
     */
    public ArrayList RemoteTAs {
      get {
        return _remote_ta;
      }
      set {
        _remote_ta = value;
      }
    }

    /**
     * This is true after Connect is called and false after
     * Disconnect is called.
     */
    volatile protected bool _running;
    volatile protected bool _send_pings;

    /** Object which we lock for thread safety */
    protected Object _sync;

    protected Thread _announce_thread;

    protected ConnectionTable _connection_table;

    /**
     * Manages the various mappings associated with connections
     */
    public virtual ConnectionTable ConnectionTable { get { return _connection_table; } }
    /**
     * Brunet BroadcastRPC service!
     */
    public BroadcastRPC BroadcastRPC { get { return _broadcastrpc; } }
    protected BroadcastRPC _broadcastrpc;
    protected CodeInjection _codeinjection;

    /**
     * This is true if the Node is properly connected in the network.
     * If you want to know when it is safe to assume you are connected,
     * listen to all for Node.ConnectionTable.ConnectionEvent and
     * Node.ConnectionTable.DisconnectionEvent and then check
     * this property.  If it is true, you should probably wait
     * until it is false if you need the Node to be connected
     */
    public abstract bool IsConnected { get; }
    protected TaskQueue _task_queue;
    /**
     * This is the TaskQueue for this Node
     */
    public TaskQueue TaskQueue { get { return _task_queue; } }

    //The timer that tells us when to HeartBeatEvent
    protected Timer _timer;

    protected int _heart_period;
    ///how many milliseconds between heartbeats
    public int HeartPeriod { get { return _heart_period; } }

    ///If we don't hear anything from a *CONNECTION* in this time, ping it.
    protected TimeSpan _connection_timeout;
    ///This is the maximum value we allow _connection_timeout to grow to
    protected static readonly TimeSpan MAX_CONNECTION_TIMEOUT = new TimeSpan(0,0,0,15,0);
    //Give edges this long to get connected, then drop them
    protected static readonly TimeSpan _unconnected_timeout = new TimeSpan(0,0,0,30,0);
    /**
     * Maximum number of TAs we keep in both for local and remote.
     * This does not control how many we send to our neighbors.
     */
    static protected readonly int _MAX_RECORDED_TAS = 10000;
    ///The DateTime that we last checked the edges.  @see CheckEdgesCallback
    protected DateTime _last_edge_check;

    ///after each HeartPeriod, the HeartBeat event is fired
    public event EventHandler HeartBeatEvent;
    
    //add an event handler which conveys the fact that Disconnect has been called on the node
    public event EventHandler DepartureEvent;

    //add an event handler which conveys the fact that Connect has been called on the node
    public event EventHandler ArrivalEvent;

    public virtual void AddEdgeListener(EdgeListener el)
    {
      /* The EdgeFactory needs to be made aware of all EdgeListeners */
      _edge_factory.AddListener(el);
      _edgelistener_list.Add(el);
      /**
       * It is ESSENTIAL that the EdgeEvent of EdgeListener objects
       * be connected to the EdgeHandler method of ConnectionPacketHandler
       */
      el.EdgeEvent += this.EdgeHandler;
    }
    /**
     * Unsubscribe all IDataHandlers for a given
     * type
     */
    protected void ClearTypeSource(PType t) {
      lock( _sync ) {
        _subscription_table.Remove(t);
      }
    }
    /**
     * The default TTL for this destination 
     */
    public virtual short DefaultTTLFor(Address destination)
    {
      short ttl;
      double ttld;
      if( destination is StructuredAddress ) {
	 //This is from the original papers on
	 //small world routing.  The maximum distance
	 //is almost certainly less than log^3 N
        ttld = Math.Log( NetworkSize );
        ttld = ttld * ttld * ttld;
      }
      else {
	//Most random networks have diameter
	//of size order Log N
        ttld = Math.Log( NetworkSize, 2.0 );
        ttld = 2.0 * ttld;
      }
      
      if( ttld < 2.0 ) {
        //Don't send too short a distance
	ttl = 2;
      }
      else if( ttld > (double)AHPacket.MaxTtl ) {
        ttl = AHPacket.MaxTtl;
      }
      else {
        ttl = (short)( ttld );
      }
      //When the network is very small this could happen, at least give it one
      //hop:
      if( ttl < 1 ) { ttl = 1; }
      return ttl;
    }

    /**
     * This Handler should be connected to incoming EdgeEvent
     * events.  If it is not, it cannot hear the new edges.
     *
     * When a new edge is created, we make sure we can hear
     * the packets from it.  Also, we make sure we can hear
     * the CloseEvent.
     *
     * @param edge the new Edge
     */
    protected void EdgeHandler(object edge, EventArgs args)
    {
      Edge e = (Edge)edge;
      e.Subscribe(this, e);
      _connection_table.AddUnconnected(e);
    }

    /**
     * All packets that come to this node are demultiplexed according to
     * type.  To subscribe, get the ISource for the type you want, and
     * subscribe to it.  Similarly for the unsubscribe.
     */
    public ISource GetTypeSource(PType t) {
      //It's safe to get from a Hashtable without a lock.
      ISource s = (ISource)_subscription_table[t];
      if( s == null ) {
        lock( _sync ) {
          //Since we last checked, there may be a ISource from another thread:
          s = (ISource)_subscription_table[t];
          if( s == null ) {
            s = new NodeSource();
            _subscription_table[t] = s;
          }
        }
      }
      return s;
    }
    /**
     * Starts all edge listeners for the node.
     * Useful for connect/disconnect operations
     */
    protected virtual void StartAllEdgeListeners()
    {
      foreach(EdgeListener el in _edgelistener_list) {
        ProtocolLog.WriteIf(ProtocolLog.NodeLog, String.Format(
          "{0} starting {1}", Address, el));

        el.Start();
      }
      _running = true;
      _announce_thread.Start();
    }

    /**
     * Stops all edge listeners for the node.
     * Useful for connect/disconnect operations
     */
    protected virtual void StopAllEdgeListeners()
    {
      foreach(EdgeListener el in _edgelistener_list) {
        el.Stop();
      }
      _edgelistener_list.Clear();
      _running = false;
      _timer.Dispose();
      //This makes sure we don't block forever on the last packet
      _packet_queue.Close();

      RpcManager.GetInstance(this).Close();
      ReqrepManager.GetInstance(this).Close();
    }
    /**
     * When we do announces using the seperate thread, this is
     * what we pass
     */
    private class AnnounceState {
      public MemBlock Data;
      public Edge From;
      public AnnounceState(MemBlock p, Edge from) {
        Data = p;
        From = from;
      }
    }
    private void AnnounceThread() {
      try {
        while( _running ) {
          AnnounceState a_state = (AnnounceState)_packet_queue.Dequeue();
          Announce(a_state.Data, a_state.From);
        }
      }
      catch(System.InvalidOperationException x) {
        //This is thrown when Dequeue is called on an empty queue
        //which happens when the BlockingQueue is closed, which
        //happens on Disconnect
        if(_running) {
          ProtocolLog.WriteIf(ProtocolLog.Exceptions, String.Format(
            "Running in AnnounceThread got Exception: {0}", x));
        }
      }
      catch(Exception x) {
        ProtocolLog.WriteIf(ProtocolLog.Exceptions, String.Format(
        "ERROR: Exception in AnnounceThread: {0}", x));
      }
    }
    /**
     * When a packet is to be delivered to this node,
     * this method is called.  This method is public so that
     * we can chain protocols through the node.  For instance,
     * after a packet is handled, it may be a wrapped packet
     * which actually contains another packet inside.  Thus,
     * the unwrapped packet could be "Announced" by the handler
     *
     * One needs to be careful to prevent an infinite loop of
     * a Handler announcing the packet it is supposed to handle.
     */
    public virtual void Announce(MemBlock b, ISender from)
    {
      //When Subscribe or unsubscribe are called,
      //they make copies of the ArrayList, thus we
      //only need to hold the sync while we are
      //getting the list of handlers.

      /* 
       * Note that getting from Hashtable is threadsafe, multiple
       * threads writing is a problem
       */
      MemBlock payload = null;
      int handlers = 0;
      NodeSource ns = null;
      PType t = null;
      try {
        t = PType.Parse(b, out payload);
        ns = (NodeSource)GetTypeSource(t);
        handlers = ns.Announce(payload, from);
        /**
         * @todo if no one handled the packet, we might want to send some
         * ICMP-like message.
         */
        if( handlers == 0 ) {
          string p_s = payload.GetString(System.Text.Encoding.ASCII);
          ProtocolLog.WriteIf(ProtocolLog.Exceptions, String.Format(
            "No Handler for packet type: {0}\n{1}", t, p_s));
        }
      }
      catch(Exception x) {
        ProtocolLog.WriteIf(ProtocolLog.Exceptions, String.Format(
          "Packet Handling Exception"));
        string nodeSource = "null";
        if (ns != null) {
          nodeSource = ns.ToString();
        }
        ProtocolLog.WriteIf(ProtocolLog.Exceptions, String.Format(
          "Handler: {0}\tEdge: {1}", nodeSource, from));
        ProtocolLog.WriteIf(ProtocolLog.Exceptions, String.Format(
          "Exception: {0}", x));
      }
    }
    /**
     * This method is called when the Node should connect to the
     * network
     */
    public virtual void Connect() {
      ProtocolLog.Enable();
      EventHandler ae = System.Threading.Interlocked.Exchange(ref ArrivalEvent, null);
      if (ae != null) {
	ae(this, null);
      } 
    }
    /**
     * Disconnect to the network.
     */
    public virtual void Disconnect() {
      ProtocolLog.WriteIf(ProtocolLog.NodeLog, String.Format(
        "[Connect: {0}] deactivating task queue", _local_add));
      _broadcastrpc.Stop();
      _task_queue.IsActive = false;
      _send_pings = false;
      //Make sure not to call DepartureEvent twice:
      EventHandler de = Interlocked.Exchange(ref DepartureEvent, null);
      if (de != null) {
        de(this, null);
      }
      _connection_table.Close();
    }

    /**
     * When a ConnectionEvent occurs, this handler registers the
     * information with the node
     */
    public virtual void ConnectionHandler(object ct, EventArgs args)
    {
      ConnectionEventArgs ce_args = (ConnectionEventArgs) args;
      Edge edge = ce_args.Edge;
      edge.Subscribe(this, edge);
      //Our peer's remote is us
      TransportAddress reported_ta =
            ce_args.Connection.PeerLinkMessage.Remote.FirstTA;
      //Our peer's local is them
      TransportAddress remote_ta =
            ce_args.Connection.PeerLinkMessage.Local.FirstTA;
      /*
       * Make a copy so that _remote_ta never changes while
       * someone is using it
       */
      ArrayList new_remote_ta = new ArrayList();
      foreach(EdgeListener el in _edgelistener_list) {
        //Update our local list:
        el.UpdateLocalTAs(edge, reported_ta);
        el.UpdateRemoteTAs( new_remote_ta, edge, remote_ta);
      }
      UpdateRemoteTAs(new_remote_ta);
    }

    /**
     * Called by ConnectionEvent and the LocalConnectionOverlord to update
     * the remote ta list.
     */
    public void UpdateRemoteTAs(ArrayList new_remote_ta)
    {
      lock( _remote_ta ) {
        foreach(TransportAddress ta in new_remote_ta) {
          if(_remote_ta.Contains(ta)) {
            _remote_ta.Remove(ta);
          }
        }
        new_remote_ta.AddRange( _remote_ta );
        int count = new_remote_ta.Count;
        if( count > _MAX_RECORDED_TAS ) {
          int rm_count = count - _MAX_RECORDED_TAS;
          new_remote_ta.RemoveRange(_MAX_RECORDED_TAS, rm_count);
        }
        _remote_ta = new_remote_ta;
      }
    }

    /**
     * Return a NodeInfo object for this node containing
     * at most max_local local Transport addresses
     */
    virtual public NodeInfo GetNodeInfo(int max_local) {
      ArrayList l = new ArrayList( this.LocalTAs );
      if( l.Count > max_local ) {
        int rm_count = l.Count - max_local;
        l.RemoveRange( max_local, rm_count );
      }
      return NodeInfo.CreateInstance( this.Address, l);
    }
    /**
     * return a status message for this node.
     * Currently this provides neighbor list exchange
     * but may be used for other features in the future
     * such as network size estimate sharing.
     * @param con_type_string string representation of the desired type.
     * @param addr address of the new node we just connected to.
     */
    virtual public StatusMessage GetStatus(string con_type_string, Address addr)
    {
      ArrayList neighbors = new ArrayList();
      //Get the neighbors of this type:
      /*
       * Send the list of all neighbors of this type.
       * @todo make sure we are not sending more than
       * will fit in a single packet.
       */
      ConnectionType ct = Connection.StringToMainType( con_type_string );
      foreach(Connection c in _connection_table.GetConnections( ct ) ) {
        neighbors.Add( NodeInfo.CreateInstance( c.Address ) );
      }
      return new StatusMessage( con_type_string, neighbors );
    }
    
    /**
     * Close the edge after we get a response CloseMessage
     * from the node on the other end.
     * This method is to try to make sure both sides of an edge
     * know that the edge is closing.
     * @param e Edge to close
     */
    public void GracefullyClose(Edge e)
    {
      GracefullyClose(e, String.Empty);
    }
    /**
     * @param e Edge to close
     * @param cm message to send to other node
     * This method is used if we want to use a particular CloseMessage
     * If not, we can use the method with the same name with one fewer
     * parameters
     */
    public void GracefullyClose(Edge e, string message)
    {
      /**
       * Close any connection on this edge, and
       * put the edge into the list of unconnected edges
       */
      _connection_table.Disconnect(e);
      
      ListDictionary close_info = new ListDictionary();
      string reason = message;
      if( reason != String.Empty ) {
        close_info["reason"] = reason;
      }
      Channel results = new Channel();
      results.CloseAfterEnqueue();
      EventHandler close_eh = delegate(object o, EventArgs args) {
        e.Close(); 
      };
      results.CloseEvent += close_eh;
      RpcManager rpc = RpcManager.GetInstance(this);
      try {
        rpc.Invoke(e, results, "sys:link.Close", close_info);
      }
      catch { e.Close(); }
    }

    /**
     * Implements the IDataHandler interface
     */
    public void HandleData(MemBlock data, ISender return_path, object state) {
      AnnounceState astate = new AnnounceState(data, return_path as Edge);
      _packet_queue.Enqueue(astate);
    }

    protected TimeSpan ComputeDynamicTimeout() {
      TimeSpan timeout;
      //Compute the mean and stddev of LastInPacketDateTime:
      double sum = 0.0;
      double sum2 = 0.0;
      int count = 0;
      DateTime now = DateTime.UtcNow;
      foreach(Connection con in _connection_table) {
        Edge e = con.Edge;
        double this_int = (now - e.LastInPacketDateTime).TotalMilliseconds;
        sum += this_int;
        sum2 += this_int * this_int;
        count++;
      }
      /*
       * Compute the mean and std.dev:
       */
      if( count > 1 ) {
        double mean = sum / count;
        double s2 = sum2 - count * mean * mean;
        double stddev = Math.Sqrt( s2 /(count - 1) );
        double timeout_d = mean + stddev;
        ProtocolLog.WriteIf(ProtocolLog.NodeLog, String.Format(
          "Connection timeout: {0}, mean: {1} stdev: {2}", timeout_d, 
          mean, stddev));
        timeout = TimeSpan.FromMilliseconds( timeout_d );
        if( timeout > MAX_CONNECTION_TIMEOUT ) {
          timeout = MAX_CONNECTION_TIMEOUT;
        }
      }
      else {
        //Keep the old timeout.  Don't let small number statistics bias us
        timeout = _connection_timeout;
      }
      return timeout;
    }
    /**
     * Check all the edges in the ConnectionTable and see if any of them
     * need to be pinged or closed.
     * This method is connected to the heartbeat event.
     */
    virtual protected void CheckEdgesCallback(object node, EventArgs args)
    {
      DateTime now = DateTime.UtcNow;
      if( now - _last_edge_check > _connection_timeout ) {
        //We are checking the edges now:
        _last_edge_check = now;
        
        //_connection_timeout = ComputeDynamicTimeout();
        _connection_timeout = MAX_CONNECTION_TIMEOUT;
        /*
         * If we haven't heard from any of these people in this time,
         * we ping them, and if we don't get a response, we close them
         */
        RpcManager rpc = RpcManager.GetInstance(this);
        foreach(Connection c in _connection_table) {
          Edge e = c.Edge;
          
          if( now - e.LastInPacketDateTime > _unconnected_timeout ) {
            //Just close it.
            e.Close();
          }
          else if( _send_pings && ( now - e.LastInPacketDateTime > _connection_timeout ) ) {

            object ping_arg = String.Empty;
            EventHandler on_close = delegate(object q, EventArgs cargs) {
              Channel qu = (Channel)q;
              if( qu.Count == 0 ) {
                /* we never got a response! */
                ProtocolLog.WriteIf(ProtocolLog.NodeLog, String.Format(
	                "On an edge timeout({1}), closing connection: {0}",
                  c, _connection_timeout));
                e.Close();
              }
              else {
                //We got a response, let's make sure it's not an exception:
                bool close = false;
                try {
                  RpcResult r = (RpcResult)qu.Dequeue();
                  object o = r.Result; //This will throw an exception if there was a problem
                  if( !o.Equals( ping_arg ) ) {
                    //Something is wrong with the other node:
                    ProtocolLog.WriteIf(ProtocolLog.NodeLog, String.Format(
                      "Ping({0}) != {1} on {2}", ping_arg, o, c));
                    close = true;
                  }
                }
                catch(Exception x) {
                  ProtocolLog.WriteIf(ProtocolLog.Exceptions, String.Format(
                    "Ping on {0}: resulted in: {1}", c, x));
                  close = true;
                }
                if( close ) { e.Close(); }
              }
            };
            Channel tmp_queue = new Channel();
            tmp_queue.CloseAfterEnqueue();
            tmp_queue.CloseEvent += on_close;
            //Do the ping
            try {
              rpc.Invoke(e, tmp_queue, "sys:link.Ping", ping_arg);
            }
            catch(Exception x) {
              if(!e.IsClosed)
                ProtocolLog.WriteIf(ProtocolLog.Exceptions, String.Format(
                  "Could not Invoke ping on: {0}, {1}", c, x));
              e.Close();
            }
          }
        }
        foreach(Edge e in _connection_table.GetUnconnectedEdges() ) {
          if( now - e.LastInPacketDateTime > _unconnected_timeout ) {
            if(ProtocolLog.Connections.Enabled)
              ProtocolLog.Write(ProtocolLog.Connections, String.Format(
                "Closed an unconnected edge: {0}", e));
            e.Close();
          }
        }
      }
      else {
        //Don't do anything for now.
      }
    }
    /**
     * A TimerCallback to send the HeartBeatEvent
     * Since the Timer can call this multiple times to run concurrently, there
     * is a lock and a running boolean to prevent multiple executions
     * concurrently.  Excessive callbacks are lost.
     */
    protected void HeartBeatCallback(object state)
    {
      lock(_heartbeat_sync) {
        if(_heartbeat_running) {
          if(ProtocolLog.NodeLog.Enabled)
            ProtocolLog.Write(ProtocolLog.NodeLog, "System must be running " +
              "slow, more than one node waiting at heartbeat");
          return;
        }
        _heartbeat_running = true;
      }

      ///Just send the event:
      try {
        if( HeartBeatEvent != null ) {
          HeartBeatEvent(this, EventArgs.Empty);
        }
      }
      catch(Exception x) {
        ProtocolLog.WriteIf(ProtocolLog.Exceptions, String.Format(
          "Exception in heartbeat: {0}", x));
      }
      _heartbeat_running = false;
    }

    /**
     * This just announces the data with the current node
     * as the return path
     */
    public void Send(ICopyable data) {
      this.Announce(MemBlock.Copy(data), this);
    }
  }
}
