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

using System;
using Brunet.Graph;

namespace Brunet.OptimalRouting {
  public enum RoutingType : ushort {
    Greedy,
    Velocity
  }

  public class OptimizedSender : ISender {
    public readonly Node SendingNode;
    public readonly Address Destination;
    public Address Source { get { return SendingNode.Address; } }
    public readonly short Hops;
    public static readonly PType Optimal = new PType(3);
    public readonly RoutingType Routing;

    public OptimizedSender(Node node, Address destination, RoutingType rt) :
      this(node, destination, rt, 0)
    {
    }

    public OptimizedSender(Node node, Address destination, RoutingType rt, short hops) {
      SendingNode = node;
      Destination = destination;
      Hops = hops;
      Routing = rt;
    }

    public void Send(ICopyable data)
    {
      AHHeader ahh = new AHHeader(Hops, 0, Source, Destination, (ushort) Routing);
      SendingNode.HandleData(MemBlock.Copy(new CopyList(ahh, data)), this, null);
    }

    public string ToUri() {
      throw new NotImplementedException();
    }
  }

  public class OptimizedHandler : IDataHandler {
    public readonly Node LocalNode;
    public readonly IRouter Router;

    public OptimizedHandler(Node node)
    {
      LocalNode = node;
      Router = new OptimizedRouter(node.Address);
    }

    public void HandleData(MemBlock data, ISender from, object state) {
      AHPacket p = new AHPacket(data);
      bool deliver_locally = false;
      Router.Route(from as Edge, p, out deliver_locally);

      if(deliver_locally) {
        ISender return_sender = new OptimizedSender(LocalNode, p.Source, (RoutingType) p.Options, p.Hops);
        LocalNode.HandleData(p.Payload, return_sender, null);
      }
    }
  }

  public class OptimizedRouter : IRouter {
    public System.Collections.IEnumerable RoutedAddressClasses { get { return new int[]{0}; } }
    public ConnectionTable ConnectionTable { set { _ct = value; } }
    protected ConnectionTable _ct;
    public readonly AHAddress LocalAddr;
    public readonly BigInteger Multiplier;
    public OptimizedRouter(Address addr) {
      LocalAddr = (AHAddress) addr;
    }

    public int Route(Edge from, AHPacket p, out bool deliver_locally)
    {
      RoutingType rt = (RoutingType) p.Options;
      switch(rt) {
        case RoutingType.Greedy:
          return GreedyRouting(from, p, out deliver_locally);
        case RoutingType.Velocity:
          return VelocityRouting(from, p, out deliver_locally);
        default:
          return GreedyRouting(from, p, out deliver_locally);
      }
    }


    protected int GreedyRouting(Edge from, AHPacket p, out bool deliver_locally)
    {
      if(LocalAddr.Equals(p.Destination)) {
        deliver_locally = true;
        return 0;
      }

      ConnectionList cl = _ct.GetConnections(ConnectionType.Structured);
      int indexof = cl.IndexOf(p.Destination);
      if(indexof >= 0) {
        cl[indexof].Edge.Send(new CopyList(OptimizedSender.Optimal, p.IncrementHops()));
        deliver_locally = false;
        return 1;
      }

      indexof = ~indexof;
      Connection left = cl[indexof];
      Connection right = cl[indexof - 1];

      AHAddress destination = (AHAddress) p.Destination;
      
      BigInteger my_dist = destination.DistanceTo(LocalAddr).abs();
      BigInteger left_dist = destination.DistanceTo((AHAddress) left.Address).abs();
      BigInteger right_dist = destination.DistanceTo((AHAddress) right.Address).abs();

      ISender sender = null;

      if(my_dist < left_dist && my_dist < right_dist) {
        deliver_locally = true;
        return 0;
      } else if(left_dist < right_dist) {
        sender = left.Edge;
      } else {
        sender = right.Edge;
      }

      sender.Send(new CopyList(OptimizedSender.Optimal, p.IncrementHops()));
      deliver_locally = false;
      return 1;
    }

    protected int VelocityRouting(Edge from, AHPacket p, out bool deliver_locally)
    {
      if(LocalAddr.Equals(p.Destination)) {
        deliver_locally = true;
        return 0;
      }

      ConnectionList cl = _ct.GetConnections(ConnectionType.Structured);
      int indexof = cl.IndexOf(p.Destination);
      if(indexof >= 0) {
        cl[indexof].Edge.Send(new CopyList(OptimizedSender.Optimal, p.IncrementHops()));
        deliver_locally = false;
        return 1;
      }
      indexof = ~indexof;

      AHAddress destination = (AHAddress) p.Destination;
      BigInteger my_dist = destination.DistanceTo(LocalAddr).abs();
      BigInteger closest_dist = my_dist;
      BigInteger max_velocity = null;
      Edge closest_edge = null;
      Edge best_edge = null;
      int i = -1;
      foreach(Connection c in cl) {
        i++;
        BigInteger c_dist = destination.DistanceTo((AHAddress) c.Address).abs();

        if(my_dist < c_dist) {
          continue;
        } else if(c_dist < closest_dist) {
          closest_dist = c_dist;
          closest_edge = c.Edge;
        }

        int latency = 0;
        GraphEdge ge = c.Edge as GraphEdge;
        if(ge != null) {
          latency = ge.Delay;
        }

        if(latency <= 0) {
          continue;
        }

        BigInteger velocity = (my_dist * AHAddress.Half) / c_dist  / latency;
        if(max_velocity == null || (velocity > max_velocity)) {
          max_velocity = velocity;
          best_edge = c.Edge;
        }
      }

      if(best_edge != null) {
        best_edge.Send(new CopyList(OptimizedSender.Optimal, p.IncrementHops()));
        deliver_locally = false;
        return 1;
      } else if(closest_edge != null) {
        closest_edge.Send(new CopyList(OptimizedSender.Optimal, p.IncrementHops()));
        deliver_locally = false;
        return 1;
      } else {
        deliver_locally = true;
        return 0;
      }
    }
  }
}
