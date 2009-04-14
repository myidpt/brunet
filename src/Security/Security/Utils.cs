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
using System.Security.Cryptography;

namespace Brunet {
  public class SecurityUtils {
    /// <summary>RSACsps are not thread safe so we make a copy of it and don't
    /// worry about sharing.</summary>
    public static RSACryptoServiceProvider ImportRSA(RSACryptoServiceProvider rsa)
    {
      RSACryptoServiceProvider new_rsa = new RSACryptoServiceProvider();
      new_rsa.ImportCspBlob(rsa.ExportCspBlob(true));
      return new_rsa;
    }
  }
}
