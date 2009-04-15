#!/usr/bin/python

import math
import sys
import types
from matplotlib import pyplot, mlab

def main():
  file = open(sys.argv[1])
  ivals = []

  while True:
    line = file.readline()

    if len(line) == 0:
      break

    vals = line.split(" ")
    for i in range(len(vals)):
      try:
        val = int(vals[i])
      except:
        continue
      ivals.append(val)

  bincount = int(math.sqrt(len(ivals)))
  n, bins, patches = pyplot.hist(ivals, bincount)
  pyplot.ylabel('Routes')
  pyplot.xlabel('Latency')
  pyplot.title('Latency')
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
