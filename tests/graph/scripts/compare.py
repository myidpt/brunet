#!/usr/bin/python

import math
import sys
import types

def main():
  file1 = open(sys.argv[1])
  file2 = open(sys.argv[2])
  same = 0
  better = []
  worse = []
  vals1ilist = []
  vals2ilist = []

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
      if val1 == val2:
        same += 1
      elif val1 < val2:
        worse.append(val1 - val2)
      elif val1 > val2:
        better.append(val1 - val2)
      vals1ilist.append(val1)
      vals2ilist.append(val2)

  print "Same: " + str(same)
  print "Better:  Count:", str(len(better)), " Mean: ", mean(better), " Stdev: ", stdev(better)
  print "Worse:  Count:", str(len(worse)), " Mean: ", mean(worse), " Stdev: ", stdev(worse)
  print "Val1:  Mean:", mean(vals1ilist), " Stdev: ", stdev(vals1ilist)
  print "Val2:  Mean:", mean(vals2ilist), " Stdev: ", stdev(vals2ilist)

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
