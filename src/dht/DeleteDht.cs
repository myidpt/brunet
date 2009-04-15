/*
Copyright (C) 2009  David Wolinsky <davidiw@ufl.edu>, University of Florida

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
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

using Brunet;

namespace Brunet.DistributedServices {
  public class DeleteDht : IDht {
    protected readonly IDht _dht;
    public string Name { get { return _dht.Name; } }
    protected Dictionary<Channel, Channel> _get_table;
    protected Dictionary<MemBlock, Dictionary<MemBlock, PutState>> _put_history;
    protected object _sync;

    protected class PutState {
      protected DateTime _delete_time;
      public MemBlock ID;
      public int TTL { get { return (int) (_delete_time - DateTime.UtcNow).TotalSeconds; } }

      public PutState(MemBlock id, int ttl) {
        ID = id;
        _delete_time = DateTime.UtcNow.AddSeconds(ttl);
      }
    }

    public DeleteDht(IDht dht)
    {
      _dht = dht;
      _get_table = new Dictionary<Channel, Channel>();
      _put_history = new Dictionary<MemBlock, Dictionary<MemBlock, PutState>>();
      _sync = new object();
    }

    public bool Online { get { return _dht.Online; } }

    /// <summary>Asynchronous create.</summary>
    /// <param name="key">Ring position where the data will be stored.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">The dht lease time for the key:value pair.</param>
    /// <param name="return">The Channel where the result will be placed.</param>
    public void AsyncCreate(MemBlock key, MemBlock value, int ttl, Channel returns)
    {
      _dht.AsyncCreate(key, RegisterValue(key, value, ttl), ttl, returns);
    }

    /// <summary>Synchronous create.</summary>
    /// <param name="key">Ring position where the data will be stored.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">The dht lease time for the key:value pair.</param>
    /// <returns>Creates return true if successful or exception if another value
    /// already exists or there are network errors in adding the entry.</returns>
    public bool Create(MemBlock key, MemBlock value, int ttl)
    {
      return _dht.Create(key, RegisterValue(key, value, ttl), ttl);
    }

    /// <summary>Asynchronous put.</summary>
    /// <param name="key">Ring position where the data will be stored.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">The dht lease time for the key:value pair.</param>
    /// <param name="return">The Channel where the result will be placed.</param>
    public void AsyncPut(MemBlock key, MemBlock value, int ttl, Channel returns)
    {
      _dht.AsyncPut(key, RegisterValue(key, value, ttl), ttl, returns);
    }

    /// <summary>Synchronous put.</summary>
    /// <param name="key">The index to store the value at.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">The dht lease time for the key:value pair.</param>
    /// <returns>Puts return true if successful or exception if there are
    /// network errors in adding the entry.</returns>
    public bool Put(MemBlock key, MemBlock value, int ttl)
    {
      return _dht.Put(key, RegisterValue(key, value, ttl), ttl);
    }

    /// <summary>Synchronous get.</summary>
    /// <param name="key">The index to look up.</param>
    /// <returns>An array of DhtGetResult type containing all the results
    /// returned.  </returns>
    public Hashtable[] Get(MemBlock key)
    {
      BlockingQueue returns = new BlockingQueue();
      AsyncGet(key, returns);

      ArrayList results = new ArrayList();
      while(true) {
        // Still a chance for Dequeue to execute on an empty closed queue 
        // so we'll do this instead.
        try {
          Hashtable result = (Hashtable) returns.Dequeue();
          results.Add(result);
        }
        catch (Exception) {
          break;
        }
      }
      return (Hashtable[]) results.ToArray(typeof(Hashtable));
    }

    /// <summary>Asynchronous get.</summary>
    /// <param name="key">The index to look up.</param>
    /// <param name="returns">The channel for where the results will be stored
    /// as they come in.</param>
    public void AsyncGet(MemBlock key, Channel returns) {
      if(!Online) {
        throw new DhtException("The Node is (going) offline, DHT is offline.");
      }

      Channel gets = new Channel();
      gets.CloseEvent += GetCloseHandler;
      lock(((ICollection) _get_table).SyncRoot) {
        _get_table[gets] = returns;
      }

      try {
        _dht.AsyncGet(key, gets);
      } catch(Exception) {
        gets.Close();
      }
    }

    protected void GetCloseHandler(object o, EventArgs args) {
      Channel queue = (Channel) o;
      Channel returns = null;
      if(!_get_table.TryGetValue(queue, out returns)) {
        return;
      }

      lock(((ICollection) _get_table).SyncRoot) {
        _get_table.Remove(queue);
      }

      if(queue.Count == 0) {
        returns.Close();
        return;
      }

      Hashtable results = new Hashtable(queue.Count);
      List<MemBlock> deletes = new List<MemBlock>();
      while(queue.Count > 0) {
        Hashtable result = (Hashtable) queue.Dequeue();
        try {
          ParseValue(result);
          bool delete = (bool) result["delete"];
          if(delete) {
            deletes.Add(MemBlock.Reference((byte[]) result["value"]));
          } else {
            results[MemBlock.Reference((byte[]) result["id"])] = result;
          }
        } catch {
        }
      }

      foreach(MemBlock delete in deletes) {
        results.Remove(delete);
      }

      foreach(Hashtable result in results.Values) {
        returns.Enqueue(result);
      }

      returns.Close();
    }

    public bool Delete(MemBlock key, MemBlock value) {
      MemBlock delete_value = null;
      int ttl = 0;
      RetrieveValue(key, value, out delete_value, out ttl);

      return _dht.Put(key, delete_value, ttl);
    }

    public void AsyncDelete(MemBlock key, MemBlock value, Channel returns) {
      MemBlock delete_value = null;
      int ttl = 0;
      RetrieveValue(key, value, out delete_value, out ttl);

      _dht.AsyncPut(key, delete_value, ttl, returns);
    }

    protected void RetrieveValue(MemBlock key, MemBlock value, out MemBlock delete_value, out int ttl) {
      PutState ps = null;

      lock(_sync) {
        Dictionary<MemBlock, PutState> key_history = null;
        if(!_put_history.TryGetValue(key, out key_history)) {
          throw new Exception("No such key to delete!");
        }

        if(!key_history.TryGetValue(value, out ps)) {
          throw new Exception("No such value to delete!");
        }

        key_history.Remove(value);
      }

      ArrayList data = new ArrayList(3);
      data.Add(true);
      data.Add(MemBlock.Reference(new byte[0]));
      data.Add(ps.ID);
      
      byte[] output = null;
      using(MemoryStream ms = new MemoryStream()) {
        AdrConverter.Serialize(data, ms);
        output = ms.ToArray();
      }

      delete_value = MemBlock.Reference(output);
      ttl = ps.TTL;
    }

    protected MemBlock RegisterValue(MemBlock key, MemBlock value, int ttl)
    {
      PutState ps = null;

      lock(_sync) {
        Dictionary<MemBlock, PutState> key_history = null;
        if(!_put_history.TryGetValue(key, out key_history)) {
          key_history = new Dictionary<MemBlock, PutState>();
          _put_history[key] = key_history;
        }

        if(!key_history.TryGetValue(value, out ps)) {
          byte[] id = new byte[20];
          Random rand = new Random();
          rand.NextBytes(id);
          ps = new PutState(MemBlock.Reference(id), ttl);
          key_history[value] = ps;
        }
      }

      ArrayList data = new ArrayList(3);
      data.Add(false);
      data.Add(ps.ID);
      data.Add(value);

      byte[] output = null;
      using(MemoryStream ms = new MemoryStream()) {
        AdrConverter.Serialize(data, ms);
        output = ms.ToArray();
      }

      return output;
    }

    protected void ParseValue(Hashtable result)
    {
      MemBlock data = MemBlock.Reference((byte[]) result["value"]);

      IList list = (IList) AdrConverter.Deserialize(data);
      result["delete"] = (bool) list[0];
      result["id"] = (byte[]) list[1];
      result["value"] = (byte[]) list[2];
    }
  }
}
