#!/usr/bin/env python
import xmlrpclib, getopt, sys
pydht = xmlrpclib.Server('http://localhost:64221/xd.rem')
#pydht = xmlrpclib.Server('http://128.227.56.152:64221/xd.rem')

#usage:
# bget.py bget.py [--output=<filename to write value to>] [--quiet (only print the value)] <key>
# you must either have a value string, or an input.

optlist, args = getopt.getopt(sys.argv[1:], "", ["output=", "quiet"])
o_d = {}
for k,v in optlist:
  o_d[k] = v

if (len(args) < 1):
  print """usage:\n
  \tbget.py [--output=<filename to write value to>] [--quiet (only print the value)] <key>"""
  sys.exit(1)

for value_dict in pydht.Get(xmlrpclib.Binary(args[0])):
  value = value_dict['value'].data
  ttl = value_dict['ttl']
  print value
