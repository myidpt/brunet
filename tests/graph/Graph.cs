/*
This program is part of Brunet, a library for autonomic overlay networks.
Copyright (C) 2008 David Wolinsky davidiw@ufl.edu, Unversity of Florida

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

using Brunet.OptimalRouting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace Brunet.Graph {
  ///<summary>Graph provides the ability to test routing algorithms for
  ///ring-based structured p2p networks</summary>
  ///<remarks>Key features provided by Graph include support for Tunnels,
  ///latency, hop count, user specified latency, tweaking of network size,
  ///near neighbor count, shortcut count.</remarks>
  public class Graph : IEdgeSendHandler {
    Dictionary<Address, GraphNode> _addr_to_node;
    List<Address> _addrs;
    Random _rand;
    Dictionary<Thread, Edge> _thread_to_current_edge;
    Dictionary<Thread, List<int>> _thread_to_delays;
    Dictionary<Thread, List<int>> _thread_to_hops;
    Dictionary<GraphNode, int> _node_to_latency_index;
    List<int> _cluster_indexes;
    List<Thread> _threads;
    Dictionary<Address, int>  _unsorted_addrs;
    List<List<int>> _latency;
    List<TunnelGraphEdge> _tunnels;
    int _crawl_node;
    bool _ahrouter;
    ushort _ahoption;

    ///</summary>Creates a new Graph for simulate routing algorithms.</summary>
    ///<param name="count">The network size not including the clusters.</param>
    ///<param name="near">The amount of connections on the left or right of a 
    ///node.</param>
    ///<param name="shortcuts">The amount of far connections had per node.</param>
    ///<param name="latency">(optional)count x count matrix containing the
    ///latency between ///two points.</param>
    ///<param name="cluster_count">A cluster is a 100 node network operating on
    ///a single point in the network.  A cluster cannot communicate directly
    ///with another cluster.</param>
    public Graph(int count, int near, int shortcuts, List<List<int>> latency,
        int cluster_count)
    {
//      BigInteger.maxLength = 5;
      _rand = new Random();
      RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider();
      _addr_to_node = new Dictionary<Address, GraphNode>(count);
      _addrs = new List<Address>(count);
      _unsorted_addrs = new Dictionary<Address, int>(count);
      _latency = latency;
      _tunnels = new List<TunnelGraphEdge>();

      // first we create our regular network
      while(_addrs.Count < count) {
        Address addr = new AHAddress(rng);
        if(_addr_to_node.ContainsKey(addr)) {
          continue;
        }
        GraphNode node = new GraphNode(addr);
        _addr_to_node[addr] = node;
        _unsorted_addrs.Add(addr, _addrs.Count);
        _addrs.Add(addr);
      }

      // then we add our clusters at random locations!
      if(cluster_count > 0) {
        _node_to_latency_index = new Dictionary<GraphNode, int>();
        _cluster_indexes = new List<int>();
      }

      int ccount = 0;
      while(ccount < cluster_count) {
        int node_count = 0;
        int node_index = _rand.Next(0, _unsorted_addrs.Count);
        _cluster_indexes.Add(node_index);
        while(node_count < 100) {
          Address addr = new AHAddress(rng);
          if(_addr_to_node.ContainsKey(addr)) {
            continue;
          }
          node_count++;
          GraphNode node = new GraphNode(addr);
          _addr_to_node[addr] = node;
          _addrs.Add(addr);
          _node_to_latency_index[node] = node_index;
        }
        ccount++;
      }

      _addrs.Sort();

      for(int i = 0; i < _addrs.Count; i++) {
        GraphNode cnode = _addr_to_node[_addrs[i]];
        ConnectionList cons = cnode.ConnectionTable.GetConnections(ConnectionType.Structured);
        // We select our left and right neighbors up to near out (so we get 2*near connections)
        // Then we check to make sure we don't already have this connection, since the other guy
        // may have added it, if we don't we create one and add it.
        for(int j = 1; j <= near; j++) {
          int left = i - j;
          if(left < 0) {
            left += _addrs.Count;
          }
          GraphNode lnode = _addr_to_node[_addrs[left]];
          if(!cons.Contains(lnode.Address)) {
            int delay = CalculateDelay(cnode, lnode);
            AddConnection(cnode, lnode, delay);
            AddConnection(lnode, cnode, delay);
          }

          int right = i+j;
          if(right >= _addrs.Count) {
            right -= _addrs.Count;
          }
          GraphNode rnode = _addr_to_node[_addrs[right]];
          // No one has this connection, let's add it to both sides.
          if(!cons.Contains(rnode.Address)) {
            int delay = CalculateDelay(cnode, rnode);
            AddConnection(cnode, rnode, delay);
            AddConnection(rnode, cnode, delay);
          }
        }
        
        // Let's add shortcuts so that we have at least the minimum number of shortcuts
        while(cnode.Shortcuts < shortcuts) {
          cons = cnode.ConnectionTable.GetConnections(ConnectionType.Structured);
          Address addr = ComputeShortcutTarget(cnode.Address);
          if(cons.Contains(addr)) {
            continue;
          }
          GraphNode snode = _addr_to_node[addr];
          cons = snode.ConnectionTable.GetConnections(ConnectionType.Structured);
          int delay = CalculateDelay(cnode, snode);
          if(delay == -1) {
            continue;
          }
          AddConnection(cnode, snode, delay);
          AddConnection(snode, cnode, delay);
          cnode.Shortcuts++;
          snode.Shortcuts++;
        }
      }
    }

    ///<summary>Calculates the delay between two nodes.</summary>
    protected int CalculateDelay(GraphNode node1, GraphNode node2)
    {
      int delay = 0;
      if(_latency != null) {
        int idx1 = 0;
        if(_unsorted_addrs.ContainsKey(node1.Address)) {
          idx1 = _unsorted_addrs[node1.Address];
        } else {
          idx1 = _node_to_latency_index[node1];
        }

        int idx2 = 0;
        if(_unsorted_addrs.ContainsKey(node2.Address)) {
          idx2 = _unsorted_addrs[node2.Address];
        } else {
          idx2 = _node_to_latency_index[node2];
        }

        if(!_unsorted_addrs.ContainsKey(node2.Address) &&
            !_unsorted_addrs.ContainsKey(node2.Address)) {
          if(idx1 == idx2) {
            return 0;
          } else {
            return -1;
          }
        }

        delay = _latency[idx1][idx2];
        if(delay < 0) {
          return -1;
        } else {
          delay /= 2;
        }
      } else {
        delay = _rand.Next(10, 240);
        if(delay % 10 == 0) {
          delay = -1;
        }
      }
      return delay;
    }


    ///<summary>Creates an edge and a connection from node2 to node1 including
    ///the edge.  Note:  this is unidirectional, this must be called twice,
    ///swapping node1 with node2 for a connection to be complete.</summary>
    protected void AddConnection(GraphNode node1, GraphNode node2, int delay)
    {
      Edge edge = null;
      if(delay == -1) {
        TunnelGraphEdge tge = new TunnelGraphEdge(node1, node2, this);
        _tunnels.Add(tge);
        edge = tge;
      } else {
        edge = new GraphEdge(node1.TransportAddress, node2.TransportAddress, delay, this);
      }
      Connection con = new Connection(edge, node2.Address, ConnectionType.Structured.ToString(), null, null);
      node1.ConnectionTable.Add(con);
    }

    public void HandleEdgeSend(Edge from, ICopyable data) {
      _thread_to_current_edge[Thread.CurrentThread] = from;
    }

    public void Action(string action, string type, int thread_count, string outfile_base)
    {
      if(type == "exact") {
        _ahrouter = true;
        _ahoption = AHPacket.AHOptions.Exact;
      } else if(type == "greedy") {
        _ahrouter = true;
        _ahoption = AHPacket.AHOptions.Greedy;
      } else if(type == "opt_greedy") {
        _ahrouter = false;
        _ahoption = (ushort) OptimalRouting.RoutingType.Greedy;
      } else if(type == "velocity") {
        _ahrouter = false;
        _ahoption = (ushort) OptimalRouting.RoutingType.Velocity;
      }

      _thread_to_current_edge = new Dictionary<Thread, Edge>();
      _thread_to_delays = new Dictionary<Thread, List<int>>();
      _thread_to_hops = new Dictionary<Thread, List<int>>();
      _threads = new List<Thread>();

      Console.WriteLine("\nBeginning {0}", action);

      for(int i = 0; i < thread_count; i++) {
        Thread t = null;
        switch(action) {
          case "AllToAll":
            t = new Thread(AllToAll);
            break;
          case "Crawl":
            _crawl_node = _rand.Next(0, _addrs.Count);
            t = new Thread(Crawl);
            break;
        } 
        _threads.Add(t);
        _thread_to_current_edge[t] = null;
        _thread_to_delays[t] = new List<int>();
        _thread_to_hops[t] = new List<int>();
      }
      for(int i = 0; i < thread_count; i++) {
        _threads[i].Start(i);
      }
      for(int i = 0; i < thread_count; i++) {
        _threads[i].Join();
      }

      int count;
      double delay_avg = Average(_thread_to_delays.Values, out count);
      double delay_stdev = StandardDeviation(_thread_to_delays.Values, delay_avg);
      double hops_avg = Average(_thread_to_hops.Values, out count);
      double hops_stdev = StandardDeviation(_thread_to_hops.Values, hops_avg);
      Console.WriteLine("Test results for {0}", type);
      Console.WriteLine("Total Data Points: {0}", count);
      Console.WriteLine("Delay Average: {0}, STDEV: {1}", delay_avg, delay_stdev);
      Console.WriteLine("Hops Average: {0}, STDEV: {1}", hops_avg, hops_stdev);
      if(outfile_base != String.Empty) {
        string filename_base = outfile_base + "." + type + "." + action + ".";
        Console.WriteLine("Writing hops to file {0}hops", filename_base);
        WriteToFile(_addrs.Count, new List<List<int>>(_thread_to_hops.Values), filename_base + "hops");
        Console.WriteLine("Writing latency to file {0}latency", filename_base);
        WriteToFile(_addrs.Count, new List<List<int>>(_thread_to_delays.Values), filename_base + "latency");
      }
    }

    protected void AllToAll(object o) {
      int me = (int) o;
      int tc = _threads.Count;
      int total = _addrs.Count;
#if DEBUG
      DateTime last_call = DateTime.UtcNow;
#endif
      for(int i = me; i < total; i += tc) {
        GraphNode cnode = _addr_to_node[_addrs[i]];
        for(int j = 0; j < total; j++) {
          Address end = _addrs[j];
#if DEBUG
          if(last_call.AddSeconds(10) < DateTime.UtcNow) {
            last_call = DateTime.UtcNow;
            Console.WriteLine("Current iteration: {0}", delay_results.Count);
          }
#endif
          SendPacket(cnode, end);
        }
      }
    }

    protected void SendPacket(GraphNode from, Address to)
    {
      Address current = from.Address;
      AHPacket p = new AHPacket(0, 100, current, to, _ahoption, "m", new byte[1]{0}, 0, 1); 
      bool deliver_locally = false;
      int delay = 0;
      int hops = 0;
      Edge current_edge = null;
      do {
        GraphNode cnode = _addr_to_node[current];
        if(_ahrouter) {
          cnode.AHRouter.Route(current_edge, p, out deliver_locally);
        } else {
          cnode.OptimizedRouter.Route(current_edge, p, out deliver_locally);
        }

        current_edge = _thread_to_current_edge[Thread.CurrentThread];
        if(!deliver_locally) {
          if(current_edge is GraphEdge) {
            GraphEdge cedge = current_edge as GraphEdge;
            delay += cedge.Delay;
          } else if(current_edge is TunnelGraphEdge) {
            TunnelGraphEdge cedge = current_edge as TunnelGraphEdge;
            delay += cedge.Delay;
          }
          current = cnode.ConnectionTable.GetConnection(current_edge).Address;
          hops++;
        }
      } while(!deliver_locally);

      _thread_to_delays[Thread.CurrentThread].Add(delay);
      _thread_to_hops[Thread.CurrentThread].Add(hops);
    }

    protected void Crawl(object o)
    {
      int me = (int) o;
      int tc = _threads.Count;
      int total = _addrs.Count;
#if DEBUG
      DateTime last_call = DateTime.UtcNow;
#endif
      GraphNode start = _addr_to_node[_addrs[_crawl_node]];
      for(int i = me; i < total; i += tc) {
        Address to = _addrs[i];
#if DEBUG
        if(last_call.AddSeconds(10) < DateTime.UtcNow) {
          last_call = DateTime.UtcNow;
          Console.WriteLine("Current iteration: {0}", i);
        }
#endif
        SendPacket(start, to);
        SendPacket(_addr_to_node[to], start.Address);
      }
    }

    public static void WriteToFile(int count, List<List<int>> data, string filename)
    {
      int partitions = data.Count;
      using(StreamWriter sw = new StreamWriter(new FileStream(filename, FileMode.Create))) {
        for(int i = 0; i < count; i++) {
          for(int j = 0; j < count; j++) {
            int val = data[i % partitions][((i / partitions) * count) + j];
            sw.Write(val);
            if(j < count - 1) {
              sw.Write(" ");
            }
          }
          if(i < count - 1) {
            sw.Write("\n");
          }
        }
      }
    }

    public double Average(IEnumerable<List<int>> data, out int count)
    {
      long total = 0;
      count = 0;
      foreach(List<int> dataset in data) {
        foreach(int point in dataset) {
          total += point;
        }
        count += dataset.Count;
      }

      return (double) total / count;
    }

    public double StandardDeviation(IEnumerable<List<int>> data, double avg)
    {
      double variance = 0;
      int count = 0;
      foreach(List<int> dataset in data) {
        foreach(int point in dataset) {
          variance += Math.Pow(point  - avg, 2.0);
        }
        count += dataset.Count;
      }

      return Math.Sqrt(variance / (count - 1));
    }

    public Address ComputeShortcutTarget(Address addr) {
      int network_size = _addrs.Count;
      double logN = (double)(Brunet.Address.MemSize * 8);
      double logk = Math.Log( (double) network_size, 2.0 );
      double p = _rand.NextDouble();
      double ex = logN - (1.0 - p)*logk;
      int ex_i = (int)Math.Floor(ex);
      double ex_f = ex - Math.Floor(ex);
      //Make sure 2^(ex_long+1)  will fit in a long:
      int ex_long = ex_i % 63;
      int ex_big = ex_i - ex_long;
      ulong dist_long = (ulong)Math.Pow(2.0, ex_long + ex_f);
      //This is 2^(ex_big):
      BigInteger big_one = 1;
      BigInteger dist_big = big_one << ex_big;
      BigInteger rand_dist = dist_big * dist_long;

      // Add or subtract random distance to the current address
      BigInteger t_add = addr.ToBigInteger();

      // Random number that is 0 or 1
      if( _rand.Next(2) == 0 ) {
        t_add += rand_dist;
      }
      else {
        t_add -= rand_dist;
      }

      BigInteger target_int = new BigInteger(t_add % Address.Full);
      if((target_int & 1) == 1) {
        target_int -= 1;
      }

      Address near_addr = new AHAddress(target_int);
      int index = _addrs.BinarySearch(near_addr);
      Address to_use = near_addr;

      if(index < 0) { 
        index = ~index;
        if(index == _addrs.Count) {
          index = 0;
        }
        AHAddress right = (AHAddress) _addrs[index];
        if(index == 0) {
          index = _addrs.Count - 1;
        }
        AHAddress left = (AHAddress) _addrs[index - 1];
        AHAddress myaddr = (AHAddress) addr;
        if(right.DistanceTo(myaddr) < left.DistanceTo(myaddr)) {
          to_use = right;
        } else {
          to_use = left;
        }
      }

      if(to_use.Equals(addr)) {
        to_use = ComputeShortcutTarget(addr);
      }

      return to_use;
    }

    public void OptimizeTunnels(TunnelGraphEdge.OptimizationType ot)
    {
      Console.WriteLine("\nOptimizing tunnels for {0}.", ot);
      foreach(TunnelGraphEdge tge in _tunnels) {
        tge.Optimize(ot);
      }
    }

    public static void Main(string[] args)
    {
      int size = 100;
      int shortcuts = 1;
      int near = 3;
      int threads = 1;
      string dataset = String.Empty;
      bool lotunnels = false;
      bool flotunnels = false;
      int clusters = 0;
      string outfile = String.Empty;

      int carg = 0;
      while(carg < args.Length) {
        String[] parts = args[carg++].Split('=');
        try {
          switch(parts[0]) {
            case "--size":
              size = Int32.Parse(parts[1]);
              break;
            case "--shortcuts":
              shortcuts = Int32.Parse(parts[1]);
              break;
            case "--near":
              near = Int32.Parse(parts[1]);
              break;
            case "--threads":
              threads = Int32.Parse(parts[1]);
              break;
            case "--dataset":
              dataset = parts[1];
              break;
            case "--outfile":
              outfile = parts[1];
              break;
            case "--lotunnels":
              lotunnels = true;
              break;
            case "--flotunnels":
              flotunnels = true;
              break;
            case "--clusters":
              clusters = Int32.Parse(parts[1]);
              break;
            default:
              throw new Exception("Invalid parameter");
          }
        } catch {
          Console.WriteLine("oops...");
        }
      }

      List<List<int>> latency = null;
      if(dataset != String.Empty) {
        ParseDataSet(dataset, out latency);
        size = latency.Count;
        shortcuts = (int) (.5 * Math.Log(size + clusters * 100, 2.0));
      }


      Console.WriteLine("Creating a graph with base size: {0}, near connections: {1}, shortcuts {2}",
          size, near, shortcuts);
      if(clusters > 0) {
        Console.WriteLine("\tWith {0} clusters at 100 nodes each.", clusters);
      }

      Graph graph = new Graph(size, near, shortcuts, latency, clusters);
      if(lotunnels) {
        graph.OptimizeTunnels(TunnelGraphEdge.OptimizationType.HalfPath);
      } else if(flotunnels) {
        graph.OptimizeTunnels(TunnelGraphEdge.OptimizationType.FullPath);
      }

      Console.WriteLine("Done populating graph...");
      Console.WriteLine("Gathering all to all data...");
      graph.OptimizeTunnels(TunnelGraphEdge.OptimizationType.None);
      graph.Action("AllToAll", "velocity", threads, outfile + ".none");
      graph.Action("AllToAll", "opt_greedy", threads, outfile + ".none");
      graph.OptimizeTunnels(TunnelGraphEdge.OptimizationType.HalfPath);
      graph.Action("AllToAll", "velocity", threads, outfile + ".half");
      graph.Action("AllToAll", "opt_greedy", threads, outfile + ".half");
      graph.OptimizeTunnels(TunnelGraphEdge.OptimizationType.FullPath);
      graph.Action("AllToAll", "velocity", threads, outfile + ".full");
      graph.Action("AllToAll", "opt_greedy", threads, outfile + ".full");
    }

    public static void ParseDataSet(string filename, out List<List<int>> data)
    {
      data = new List<List<int>>();
      using(StreamReader fs = new StreamReader(new FileStream(filename, FileMode.Open))) {
        string line = null;
        while((line = fs.ReadLine()) != null) {
          string[] points = line.Split(' ');
          List<int> current = new List<int>(points.Length);
          foreach(string point in points) {
            int val;
            if(!Int32.TryParse(point, out val)) {
              continue;
            }
            current.Add(Int32.Parse(point));
          }
          data.Add(current);
        }
      }
    }
  }
}
