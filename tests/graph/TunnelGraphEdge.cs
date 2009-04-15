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

namespace Brunet.Graph {
  public class TunnelGraphEdge: Edge {
    public enum OptimizationType {
      None,
      FullPath,
      HalfPath
    }

    protected GraphNode _local_node, _remote_node;
    public int _delay;
    public int Delay {
      get {
        if(_delay == Int32.MaxValue) {
          Optimize(_ot);
        }
        if(_delay == Int32.MaxValue) {
          return -1;
        }
        return _delay;
      }
    }

    protected OptimizationType _ot;

    public TunnelGraphEdge(GraphNode local_node, GraphNode remote_node, IEdgeSendHandler callback) :
      base(callback, false)
    {
      _ot = OptimizationType.None;
      _local_node = local_node;
      _remote_node = remote_node;
      _delay = Int32.MaxValue;
    }

    public override Brunet.TransportAddress LocalTA
    {
      get
      {
        return _local_node.TransportAddress;
      }
    }

    public override Brunet.TransportAddress RemoteTA
    {
      get
      {
        return _remote_node.TransportAddress;
      }
    }

    public override Brunet.TransportAddress.TAType TAType
    {
      get
      {
        return Brunet.TransportAddress.TAType.Tcp;
      }
    }

    protected void NoOptimization()
    {
      ConnectionList local_cons = _local_node.ConnectionTable.GetConnections(ConnectionType.Structured);
      ConnectionList remote_cons = _remote_node.ConnectionTable.GetConnections(ConnectionType.Structured);
      int delay = 0;
      _delay = Int32.MaxValue;
      int forwarders = 0;
      foreach(Connection con in local_cons) {
        int index = remote_cons.IndexOf(con.Address);
        if(index < 0) {
          continue;
        }

        GraphEdge edge = con.Edge as GraphEdge;
        if(edge == null) {
          continue;
        }

        Connection rcon = remote_cons[index];
        GraphEdge redge = rcon.Edge as GraphEdge;
        if(redge == null) {
          continue;
        }

        delay += edge.Delay + redge.Delay;
        forwarders++;
      }
      if(forwarders == 0) {
        throw new Exception("Could not find a cross section!");
      } else {
        _delay = delay / forwarders;
      }
    }

    ///<summary>This provides a fully optimized tunnel between two edges, whereas
    ///the HalfPath only looks at the first hop for sending the message.</summary>
    protected void FullPathOptimization()
    {
      _delay = Int32.MaxValue;
      ConnectionList local_cons = _local_node.ConnectionTable.GetConnections(ConnectionType.Structured);
      ConnectionList remote_cons = _remote_node.ConnectionTable.GetConnections(ConnectionType.Structured);
      foreach(Connection con in local_cons) {
        int index = remote_cons.IndexOf(con.Address);
        if(index < 0) {
          continue;
        }

        GraphEdge edge = con.Edge as GraphEdge;
        if(edge == null) {
          continue;
        }

        Connection rcon = remote_cons[index];
        GraphEdge redge = rcon.Edge as GraphEdge;
        if(redge == null) {
          continue;
        }

        int delay = edge.Delay + redge.Delay;
        if(delay < _delay) {
          _delay = delay;
        }
      }
      if(_delay == Int32.MaxValue) {
        throw new Exception("Could not find a cross section!");
      }
    }

    protected void HalfPathOptimization()
    {
      _delay = Int32.MaxValue;
      ConnectionList local_cons = _local_node.ConnectionTable.GetConnections(ConnectionType.Structured);
      ConnectionList remote_cons = _remote_node.ConnectionTable.GetConnections(ConnectionType.Structured);
      int delay = Int32.MaxValue;
      foreach(Connection con in local_cons) {
        int index = remote_cons.IndexOf(con.Address);
        if(index < 0) {
          continue;
        }

        GraphEdge edge = con.Edge as GraphEdge;
        if(edge == null) {
          continue;
        }

        Connection rcon = remote_cons[index];
        GraphEdge redge = rcon.Edge as GraphEdge;
        if(redge == null) {
          continue;
        }

        if(edge.Delay < delay) {
          delay = edge.Delay;
          _delay = delay + redge.Delay;
        }
      }
      if(_delay == Int32.MaxValue) {
        throw new Exception("Could not find a cross section!");
      }
    }

    public void Optimize(OptimizationType ot)
    {
      _ot = ot;
      if(ot == OptimizationType.FullPath) {
        FullPathOptimization();
      } else if(ot == OptimizationType.HalfPath) {
        HalfPathOptimization();
      } else {
        NoOptimization();
      }
    }

    public override void Send(ICopyable p)
    {
      if(_delay == Int32.MaxValue) {
        Optimize(_ot);
      }

      _send_cb.HandleEdgeSend(this, p);
    }
  }
}
