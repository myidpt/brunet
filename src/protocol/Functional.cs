/*
This program is part of BruNet, a library for the creation of efficient overlay
networks.
Copyright (C) 2007 P. Oscar Boykin <boykin@pobox.com>,  University of Florida

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

#if BRUNET_NUNIT
using NUnit.Framework;
#endif

namespace Brunet {

/**
 * A collection of static pure functions to do some functional
 * programming
 */
#if BRUNET_NUNIT
[TestFixture]
#endif
public class Functional {

  static public ArrayList Add(ArrayList l, object o) {
    ArrayList copy = (ArrayList)l.Clone();
    copy.Add(o);
    return ArrayList.ReadOnly(copy);
  }

  static public Hashtable Add(Hashtable h, object k, object v) {
    Hashtable copy = (Hashtable)h.Clone();
    copy.Add(k, v);
    return copy;
  }

  static public ArrayList Insert(ArrayList l, int pos, object o) {
    ArrayList copy = (ArrayList)l.Clone();
    copy.Insert(pos, o);
    return ArrayList.ReadOnly(copy);
  }
  static public ArrayList RemoveAt(ArrayList l, int pos) {
    ArrayList copy = (ArrayList)l.Clone();
    copy.RemoveAt(pos);
    return ArrayList.ReadOnly(copy);
  }
  static public Hashtable Remove(Hashtable h, object k) {
    Hashtable copy = (Hashtable)h.Clone();
    copy.Remove(k);
    return copy;
  }

  static public Hashtable SetElement(Hashtable h, object k, object v) {
    Hashtable copy = (Hashtable)h.Clone();
    copy[k] = v;
    return copy;
  }
  static public ArrayList SetElement(ArrayList l, int k, object v) {
    ArrayList copy = (ArrayList)l.Clone();
    copy[k] = v;
    return ArrayList.ReadOnly(copy);
  }
  #if BRUNET_NUNIT
  [Test]
  public void Test() {
    const int TEST_LENGTH = 1000;

    ArrayList l = new ArrayList();
    ArrayList mut = new ArrayList();
    Random r = new Random();
    for(int i = 0; i < TEST_LENGTH; i++ ) {
      int j = r.Next();
      l = Add(l, j);
      mut.Add(j);
    }
    Assert.AreEqual(l.Count, mut.Count, "List count");
    for(int i = 0; i < TEST_LENGTH; i++) {
      Assert.AreEqual(l[i], mut[i], "element equality");
    }
    //Do a bunch of random sets:
    for(int i = 0; i < TEST_LENGTH; i++) {
      int j = r.Next(TEST_LENGTH);
      int k = r.Next();
      l = SetElement(l, j, k);
      mut[j] = k;
    }
    for(int i = 0; i < TEST_LENGTH; i++) {
      Assert.AreEqual(l[i], mut[i], "element equality after sets");
    }
  }
  #endif
}

}
