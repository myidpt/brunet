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
  public class GraphEdge:Brunet.Edge {
    private TransportAddress local_add;
    private TransportAddress remote_add;
    public readonly int Delay;

    public GraphEdge(TransportAddress local, TransportAddress remote, int delay, IEdgeSendHandler callback) :
      base(callback, false)
    {
      local_add = local;
      remote_add = remote;
      Delay = delay;
    }

    public override Brunet.TransportAddress LocalTA
    {
      get
      {
        return local_add;
      }
    }
    public override Brunet.TransportAddress RemoteTA
    {
      get
      {
        return remote_add;
      }
    }

    public override Brunet.TransportAddress.TAType TAType
    {
      get
      {
        return Brunet.TransportAddress.TAType.Tcp;
      }
    }

    public override void Send(ICopyable p)
    {
      _send_cb.HandleEdgeSend(this, p);
    }
  }
}