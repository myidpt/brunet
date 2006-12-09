/*
This program is part of BruNet, a library for the creation of efficient overlay
networks.
Copyright (C) 2006 P. Oscar Boykin <boykin@pobox.com>, University of Florida

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

#define DEBUG

using System;
using System.Net;
using System.Collections;

namespace Brunet {

/**
 * Sometimes we learn things about the NAT we may be behind.  This
 * class represents the data we learn
 */
public abstract class NatDataPoint {

  protected DateTime _date;
  public DateTime DateTime { get { return _date; } }
  
  protected TransportAddress _local;
  public TransportAddress LocalTA { get { return _local; } }

  protected TransportAddress _p_local;
  public TransportAddress PeerViewOfLocalTA { get { return _p_local; } }
  
  protected TransportAddress _remote;
  public TransportAddress RemoteTA { get { return _remote; } }
  
  protected TransportAddress _old_ta;
  /**
   * When the mapping changes, this is the previous TA
   */
  public TransportAddress PreviousTA { get { return _old_ta; } }

  protected int _edge_no;
  /**
   * So we don't keep a reference to the Edge, thereby potentially never allowing
   * garbage collection, each Edge is assigned a unique number
   * This is a unique mapping for the life of the Edge
   */
  public int EdgeNumber { get { return _edge_no; } }

  static protected WeakHashtable _edge_nos;
  static int _next_edge_no;
  static NatDataPoint() {
    _edge_nos = new WeakHashtable();
    _next_edge_no = 1;
  }

  /**
   * Return the edge number for the given Edge.  If we don't
   * have a number for it, return 0
   */
  static public int GetEdgeNumberOf(Edge e) {
    int no = 0;
    lock( _edge_nos ) {
      object v = _edge_nos[e];
      if( v != null ) {
        no = (int)v;
      }
    }
    return no;
  }

  protected void SetEdgeNumber(Edge e) {
    if( e == null ) {
      _edge_no = 0;
    }
    else {
     lock( _edge_nos ) {
      object v = _edge_nos[e];
      if( v != null ) {
        _edge_no = (int)v;
      }
      else {
        _edge_no = _next_edge_no;
        _next_edge_no++;
        _edge_nos[e] = _edge_no;
      }
     }
    }
  }
}

/**
 * When a NewEdge is created this is the point
 */
public class NewEdgePoint : NatDataPoint {
  public NewEdgePoint(DateTime dt, Edge e) {
    _date = dt;
    SetEdgeNumber(e);
    _local = e.LocalTA;
    _remote = e.RemoteTA;
  }
}

/**
 * When an Edge closes, we note it
 */
public class EdgeClosePoint : NatDataPoint {
  public EdgeClosePoint(DateTime dt, Edge e) {
    _date = dt;
    SetEdgeNumber(e);
    _local = e.LocalTA;
    _remote = e.RemoteTA;
  }
}

/**
 * When the local mapping changes, record it here:
 */
public class LocalMappingChangePoint : NatDataPoint {
  public LocalMappingChangePoint(DateTime dt, Edge e,
                                 TransportAddress new_ta) {
    _date = dt;
    SetEdgeNumber(e);
    _local = e.LocalTA;
    _remote = e.RemoteTA;
    _p_local = new_ta;
  }
}

/**
 * When the local mapping changes, record it here:
 */
public class RemoteMappingChangePoint : NatDataPoint {
  public RemoteMappingChangePoint(DateTime dt, Edge e) {
    _date = dt;
    SetEdgeNumber(e);
    _local = e.LocalTA;
    _remote = e.RemoteTA;
  }
}

/**
 * The ordered list of all the NatDataPoint objects
 * provides several methods to make selecting subsets easier
 */
public class NatHistory : IEnumerable {

  /**
   * Given a data point, return some object which is a function
   * of it.
   * if this function returns null, the output will be skipped
   */
  public delegate object Filter(NatDataPoint p);

  //We store the data points in a linked list:
  protected class LLPoint {
    public NatDataPoint NDP;
    public LLPoint Prev;
  }

  protected LLPoint _head;

  public NatHistory() {
    _head = null;
  }
  public NatHistory(NatDataPoint p) {
    _head = new LLPoint();
    _head.NDP = p;
    _head.Prev = null;
  }
 
  /**
   * Returns the most recent NatDataPoint, or null if empty
   */
  public NatDataPoint Head {
    get {
      if( _head != null ) {
        return _head.NDP;
      }
      else {
        return null;
      }
    }
  }

  /**
   * This goes from most recent to least recent data point
   */
  public IEnumerator GetEnumerator() {
    LLPoint tmp_p = _head;
    while( tmp_p != null ) {
      yield return tmp_p.NDP;
      tmp_p = tmp_p.Prev;
    }
  }

  /**
   * Return an IEnumerable of NatDataPoints which is all the points
   * where f is true.
   */
  public IEnumerable FilteredEnumerator(Filter f) {
    return new FilteredNDP(this, f);
  }

  /**
   * Given an IEnumerable of NatDataPoints, you can filter it to create
   * another.
   * Only the non-null returned values from the Filter will be returned
   * in the IEnumerator
   */
  public class FilteredNDP : IEnumerable {
    protected Filter _filter;
    protected IEnumerable _ie;
    public FilteredNDP(IEnumerable ie, Filter f) {
      _ie = ie;
      _filter = f;
    }

    public IEnumerator GetEnumerator() {
      foreach(NatDataPoint ndp in _ie) {
        object o = _filter(ndp);
        if( o != null ) {
          yield return o;
        }
      }
    }
  }

  /**
   * Gets a list of all the IPAddress objects which may
   * represent NATs we are behind
   */
  public IEnumerable PeerViewIPs() {
    Filter f = delegate(NatDataPoint p) {
      TransportAddress ta = p.PeerViewOfLocalTA;
      if( ta != null ) {
        return ta.GetIPAddresses()[0];
      }
      return null;
    };
    IEnumerable e = new FilteredNDP( this, f );
    /*
     * Go through all the addresses, but only keep
     * one copy of each, with the most recent address last.
     */
    ArrayList list = new ArrayList();
    foreach(IPAddress a in e) {
      if( list.Contains(a) ) {
        list.Remove(a);
      } 
      list.Add(a);
    }
    return list;
  }

  /**
   * An IEnumerator of all the LocalTAs (our view of them, not peer view)
   */
  public IEnumerable LocalTAs() {
    Hashtable ht = new Hashtable();
    Filter f = delegate(NatDataPoint p) {
      TransportAddress ta = p.LocalTA;
      if( ta != null && (false == ht.Contains(ta)) ) {
        ht[ta] = true;
        return ta;
      }
      return null;
    };
    return new FilteredNDP( this, f );
  }

  /**
   * Give all the NatDataPoints that have a PeerViewOfLocalTA matching the
   * giving IPAddress
   */
  public IEnumerable PointsForIP(IPAddress a) {
    Filter f = delegate(NatDataPoint p) {
      TransportAddress ta = p.PeerViewOfLocalTA;
      if( ta != null ) {
        if( a.Equals( ta.GetIPAddresses()[0] ) ) {
          return p;
        }
      }
      return null;
    };
    return new FilteredNDP(this, f);
  }

  /**
   * Makes a new history and returns it, this DOES NOT CHANGE
   * the current NatHistory
   */
  public NatHistory Add(NatDataPoint p) {
    LLPoint lp = new LLPoint();
    lp.Prev = _head;
    lp.NDP = p;
    NatHistory hist = new NatHistory();
    hist._head = lp;
    return hist;
  }

}


/**
 * All NatHandlers are subclasses of this class.
 */
public abstract class NatHandler {
  
  /**
   * @return true if the handler can handle this kind of NAT
   */
  abstract public bool IsMyType(IEnumerable hist);

  /**
   * @return a list of TAs which should correspond to the local NAT
   */
  virtual public IList TargetTAs(IEnumerable hist) {
    //Put each TA in once, but the most recently used ones should be first:
    ArrayList tas = new ArrayList();
    foreach(NatDataPoint np in hist) {
      TransportAddress ta = np.PeerViewOfLocalTA;
      if( ta != null ) {
        if( !tas.Contains(ta) ) {
          //If we haven't already seen this, put it in
          tas.Add( ta );
        }
      }
    }
    return tas;  
  }

}

/**
 * This is the case where the node is not behind any NAT whatsoever
 * This will only work when the peer views all match some local view
 * of the TA.
 */
public class PublicNatHandler : NatHandler {
  override public bool IsMyType(IEnumerable h) {
    //First make a hashtable of the local views:
    Hashtable ht = new Hashtable();
    foreach(NatDataPoint p in h) {
      ht[p.LocalTA] = true;
    }
    //Now check to see the peer view is a local ta:
    bool retv = true;
    foreach(NatDataPoint p in h) {
      TransportAddress pv = p.PeerViewOfLocalTA;
      if( pv != null ) {
        retv = retv && ht.Contains(pv);
        if( false == retv ) {
          break;
        }
      }
    }
    return retv;
  }
}

/**
 * This is some kind of default handler which is a last resort mode
 * The algorithm here is to just return the full history of reported
 * TAs or localTAs in the order of most recently to least recently used
 */
public class NullNatHandler : NatHandler {

  /**
   * This NatHandler thinks it can handle anything.
   */
  override public bool IsMyType(IEnumerable h) { return true; }
  
}

/**
 * Handles Cone Nats
 */
public class ConeNatHandler : NatHandler {
  /**
   * The cone nat uses exactly one port on each IP address
   * @todo handle NAT mapping changes which can occasionally occur on a Cone NAT
   */
  override public bool IsMyType(IEnumerable h) {
    bool got_first = false;
    int port = 0;
    foreach( NatDataPoint dp in h ) {
        if( !got_first ) {
          port = dp.PeerViewOfLocalTA.Port;
          got_first = true;
        }
        else {
          if( port != dp.PeerViewOfLocalTA.Port ) {
            //There are several ports on the IP mapping to our address
            return false;
          }
        }
    }
    return true;
  }

  /**
   * return the list of TAs that should be tried
   */
  override public IList TargetTAs(IEnumerable hist) {
      /*
       * The trick here is, for a cone nat, we should only report
       * the most recently used ip/port pair.  Not more than one
       * port for a given ip.
       */
      ArrayList tas = new ArrayList();
      foreach(NatDataPoint p in hist) {
        TransportAddress last_reported = p.PeerViewOfLocalTA;
        if( last_reported != null ) {
          tas.Add( last_reported );
          return tas;
        }
      }
      return tas;
  }
}

public class SymmetricNatHandler : NatHandler {

  ///How many std. dev. on each side of the mean to use
  protected static readonly double SAFETY = 2.0;

  override public bool IsMyType(IEnumerable h) {
    ArrayList l = PredictPorts(h); 
    //If our prediction gives a narrow enough range, it must be good:
    return ( (0 < l.Count) && (l.Count < 15) );
  }

  /*
   * Given an IEnumerable of NatDataPoints, return a list of 
   * ports from most likely to least likely to be the
   * next port used by the NAT
   */
  protected ArrayList PredictPorts(IEnumerable ndps) {
    ArrayList all_diffs = new ArrayList();
    //Get an increasing subset of the ports:
    int prev = Int32.MinValue; 
    uint sum = 0;
    uint sum2 = 0;
    bool got_extra_data = false;
    TransportAddress.TAType t = TransportAddress.TAType.Unknown;
    string host = "";
    foreach(NatDataPoint ndp in ndps) {
      if( false == (ndp is EdgeClosePoint) ) {
        //Ignore closing events for prediction, they'll screw up the port prediction
        TransportAddress ta = ndp.PeerViewOfLocalTA;
        if( ta != null ) {
          int port = ta.Port;
          if( !got_extra_data ) {
            t = ta.TransportAddressType;
            host = ta.Host;
          }
          if( prev > port ) {
            uint diff = (uint)(prev - port); //Clearly diff is always non-neg
            all_diffs.Add( diff );
            sum += diff;
            sum2 += diff * diff;
          }
          prev = port;
        }
      }
    }
    /**
     * Now look at the mean and variance of the diffs
     */
    ArrayList prediction = new ArrayList();
    if( all_diffs.Count > 1 ) {
      double n = (double)all_diffs.Count;
      double sd = (double)sum;
      double mean = sd/n;
      double s2 = ((double)sum2) - sd*sd/n;
      s2 = s2/(double)(all_diffs.Count - 1);
      double stddev = Math.Sqrt(s2);
      double max_delta = mean + SAFETY * stddev;
      int delta = (int)(mean - SAFETY * stddev);
      while(delta < max_delta) {
        if( delta > 0 ) {
          int pred_port = prev + delta;
          prediction.Add(new TransportAddress(t, host, pred_port) );
        }
        else {
          //Increment the max by one just to keep a constant width:
          max_delta += 1.001; //Giving a little extra to make sure we get 1
        }
        delta++;
      }
    }
    return prediction;
  }

  override public IList TargetTAs(IEnumerable hist) {
    return PredictPorts(hist); 
  }

}

/**
 * The standard IPTables NAT in Linux is similar to a symmetric NAT.
 * It will try to avoid translating the port number, but if it can't
 * (due to another node behind the NAT already using that port to contact
 * the same remote IP/port), then it will assign a new port. 
 *
 * So, we should try to use the "default" port first, but if that doesn't
 * work, use port prediction.
 */
public class LinuxNatHandler : SymmetricNatHandler {
  
  /**
   * Check to see that at least some of the remote ports match the local
   * port
   */
  override public bool IsMyType(IEnumerable h) {
    bool retv = false;
    MakeTargets(h, out retv);
    return retv;
  }
  
  protected IList MakeTargets(IEnumerable h, out bool success) {
    bool there_is_a_match = false;
    int matched_port = 0;
    TransportAddress matched_ta = null;
    foreach(NatDataPoint p in h) {
      TransportAddress l = p.LocalTA;
      TransportAddress pv = p.PeerViewOfLocalTA;
      if( l != null && pv != null ) {
        there_is_a_match = (l.Port == pv.Port);
        if( there_is_a_match ) {
          //Move on.
          matched_port = l.Port;
          matched_ta = pv;
          break;
        }
      }
    }
    if( there_is_a_match ) {
      //Now we filter to look at only the unmatched ports:
      NatHistory.Filter f = delegate(NatDataPoint p) {
        TransportAddress pv = p.PeerViewOfLocalTA;
        if( (pv != null) && (pv.Port != matched_port) ) {
          return p;
        }
        return null;
      };
      //This is all the non-matching data points:
      IEnumerable non_matched = new NatHistory.FilteredNDP(h, f);
      ArrayList l = PredictPorts( non_matched );
      //Put in the matched port at the top of the list:
      l.Insert(0, matched_ta);
      success = true;
      return l;
    }
    else {
      success = false;
      return null;
    }
  }

  public override IList TargetTAs(IEnumerable h) {
    bool success = false;
    IList result = MakeTargets(h, out success);
    if( success ) {
      return result;
    }
    else {
      return new ArrayList();
    }
  }

}

/**
 * This is an enumerable object to create the TAs for a given history
 */
public class NatTAs : IEnumerable {

  protected NatHistory _hist;
  protected ArrayList _list_of_remote_ips;
  protected IEnumerable _local_config;
  protected IEnumerable _generated_ta_list;

  /**
   * @param local_config_tas the list of TAs to use as last resort
   * @param NatHistory history information learned from talking to peers
   */
  public NatTAs(IEnumerable local_config_tas, NatHistory hist) {
    _hist = hist;
    _local_config = local_config_tas;
  }
  protected void InitRemoteIPs() {
    NatHistory.Filter f = delegate(NatDataPoint p) {
      TransportAddress ta = p.PeerViewOfLocalTA;
      if( ta != null ) {
        return ta.GetIPAddresses()[0];
      }
      return null;
    };
    IEnumerable all_ips = _hist.FilteredEnumerator(f);
    Hashtable ht = new Hashtable();
    foreach(IPAddress a in all_ips) {
      if( false == ht.Contains(a) ) {
        IPAddressRecord r = new IPAddressRecord();
        r.IP = a;
        r.Count = 1;
        ht[a] = r;
      }
      else {
        IPAddressRecord r = (IPAddressRecord)ht[a];
        r.Count++;
      }
    }
    
    _list_of_remote_ips = new ArrayList();  
    IDictionaryEnumerator de = ht.GetEnumerator();
    while(de.MoveNext()) {
      IPAddressRecord r = (IPAddressRecord)de.Value;
      _list_of_remote_ips.Add(r);
    }
    //Now we have a list of the most used to least used IPs
    _list_of_remote_ips.Sort();
  }

  protected void GenerateTAs() {
    /*
     * we go through the list from most likely to least likely:
     */
    if( _list_of_remote_ips == null ) {
      InitRemoteIPs();
    }
    ArrayList gtas = new ArrayList();
    Hashtable ht = new Hashtable();
    foreach(IPAddressRecord r in _list_of_remote_ips) {
      IEnumerable points = _hist.PointsForIP(r.IP);
      IEnumerator hand_it = NatTAs.AllHandlers();
      bool yielded = false;
      while( hand_it.MoveNext() && (false == yielded) ) {
        NatHandler hand = (NatHandler)hand_it.Current;
        if( hand.IsMyType( points ) ) {
#if DEBUG
          System.Console.WriteLine("NatHandler: {0}", hand.GetType() );
#endif
          IList tas = hand.TargetTAs( points );
          foreach(TransportAddress ta in tas) {
            if( false == ht.Contains(ta) ) {
              ht[ta] = true;
              gtas.Add(ta);
            }
          }
          //Break out of the while loop, we found the handler.
          yielded = true;
        }
      }
    }
    //Now we should yield the locally configured points:
    foreach(TransportAddress ta in _local_config) {
      if( false == ht.Contains(ta) ) {
        //Don't yield the same address more than once
        gtas.Add(ta);
      }
    }
    _generated_ta_list = gtas; 
  }

  /**
   * This is the main method, this enumerates (in order) the
   * TAs for this history
   */
  public IEnumerator GetEnumerator() {
    if( _generated_ta_list == null ) {
      GenerateTAs();
    }
    return _generated_ta_list.GetEnumerator();
  }

  /**
   * Enumerator that will go through all the NatHandlers in a fixed order
   */
  static protected IEnumerator AllHandlers() {
    //The ConeNatHandler can handle public nodes, so don't bother with public
    //yield return new PublicNatHandler();
    yield return new ConeNatHandler();
    yield return new LinuxNatHandler();
    yield return new SymmetricNatHandler();
    yield return new NullNatHandler();
  }

  protected class IPAddressRecord : IComparable {
    public IPAddress IP;
    public int Count;
    /**
     * Sort them from largest count to least count
     */
    public int CompareTo(object o) {
      if( this == o ) { return 0; }
      IPAddressRecord other = (IPAddressRecord)o;
      if( Count > other.Count ) {
        return -1;
      }
      else if( Count < other.Count ) {
        return 1;
      }
      else {
        return 0;
      }
    }
  }

}


}
