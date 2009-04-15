#!/usr/bin/python

import math
import sys
import types
from matplotlib import pyplot, mlab

def main():
  file1 = open(sys.argv[1])
  file2 = open(sys.argv[2])
  ivals = []

  while True:
    line1 = file1.readline()
    line2 = file2.readline()

    if len(line1) == len(line2) == 0:
      break

    vals1 = line1.split(" ")
    vals2 = line2.split(" ")
    if len(vals1) != len(vals2):
      print "Invalid data sets"
      sys.exit()
    for i in range(len(vals1)):
      try:
        val1 = int(vals1[i])
        val2 = int(vals2[i])
      except:
        continue
      if(val1 == val2):
        continue
      ivals.append(val1 - val2)

  bincount = int(math.sqrt(len(ivals)))
  n, bins, patches = pyplot.hist(ivals, bincount)
  pyplot.ylabel('Routes')
  pyplot.xlabel('Latency Difference')
  pyplot.title('Greedy vs Velocity Routing: Latency Difference')
  pyplot.show()

def mean(ilist):
  if(len(ilist) == 0):
    return 0
  total = 0
  for i in ilist:
    total += i
  return float(total) / len(ilist)

def stdev(ilist):
  if(len(ilist) == 0):
    return 0
  avg = mean(ilist)
  variance = 0.0
  for i in ilist:
    variance += math.pow(i - avg, 2)
  return math.sqrt(variance / (len(ilist) - 1))

if __name__ == "__main__":
  main()
