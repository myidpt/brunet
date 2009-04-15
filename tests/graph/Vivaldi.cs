using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Brunet.Graph {
  public class Point {
    public static readonly int DEFAULT_DIMENSIONS = 2;
    protected static readonly Random _rand;

    protected double[] _coordinates;
    public double[] Coordinates {
      get {
        return (double[]) _coordinates.Clone();
      }
    }

    protected double _height;
    public double Height {
      get {
        return _height;
      }
    }

    static Point()
    {
      _rand = new Random();
    }

    public Point(double[] coordinates, double height)
    {
      if(coordinates.Length != DEFAULT_DIMENSIONS) {
        throw new Exception(String.Format("Invalid dimensions: {0}, Expected: {1}",
            coordinates.Length, DEFAULT_DIMENSIONS));
      }
      _coordinates = (double[]) coordinates.Clone();
      _height = height;
    }

    public Point()
    {
      _coordinates = new double[DEFAULT_DIMENSIONS];
      for(int i = 0; i < _coordinates.Length; i++) {
        _coordinates[i] = 0;
      }

      _height = 0;
    }

    public void Add(Point p)
    {
      for(int i = 0; i < DEFAULT_DIMENSIONS; i++) {
        _coordinates[i] += p.Coordinates[i];
      }

      _height += p.Height;
    }

    // Stir the pot a little
    public void Bump()
    {
      for(int i = 0; i < DEFAULT_DIMENSIONS; i++) {
        _coordinates[i] = (double) _rand.Next(-1000, 1000);
      }
    }

    // I guess it is unreasonable for everyone to have no height!
    public void CheckHeight()
    {
      if(_height <= 100) {
        _height = 100;
      } else if (_height > 10000) {
        _height = 10000;
      }
    }

    // Distance is defined as the Euclidean distance between the coordinates
    // plus both the heights.
    public double EuclideanDistance(Point p)
    {
      double sum = 0;
      for(int i = 0; i < DEFAULT_DIMENSIONS; i++) {
        double part = _coordinates[i] - p.Coordinates[i];
        sum += part * part;
      }

      sum = Math.Sqrt(sum) + _height + p.Height;
      return sum;
    }

    public double PlaneNorm()
    {
      double sum = 0;
      for(int i = 0; i < DEFAULT_DIMENSIONS; i++) {
        sum += _coordinates[i] * _coordinates[i];
      }
      return Math.Sqrt(sum);
    }

    public double Norm()
    {
      return PlaneNorm() + _height;
    }

    public void ScalarMutliply(double s)
    {
      for(int i = 0; i < DEFAULT_DIMENSIONS; i++) {
        _coordinates[i] *= s;
      }
      _height *= s;
    }

    public void Subtract(Point p)
    {
      for(int i = 0; i < DEFAULT_DIMENSIONS; i++) {
        _coordinates[i] -= p.Coordinates[i];
      }

      _height += p.Height;
    }
  }

  public class VivaldiPoint : Point {
    protected double _error;
    public double Error {
      get {
        return _error;
      }
    }

    public VivaldiPoint(double[] coordinates, double height) :
      base(coordinates, height)
    {
      _error = 1;
    }

    public VivaldiPoint() : base()
    {
      _error = 1;
    }

    public void Process(double latency, VivaldiPoint rp)
    {
      // obviously a buggy node
      if(rp.Error < 0 || rp.Error > 1) {
        return;
      }

      double distance = EuclideanDistance(rp);
      double latency_distance_diff = latency - distance;

      double relative_error = Math.Abs(latency_distance_diff) / latency;
      // This punishes higher error nodes more
      double le = _error * _error;
      double re = rp.Error * rp.Error;
      double new_error = relative_error * (le / (re + le)) + _error * (re / (re + le));
      _error = (19.0 * _error + new_error) / 20.0;
      if(_error > 1) {
        _error = 1;
      } else if(_error < 0) {
        _error = 0;
      }

      // this will be what changes our coordinates, this differs from their code, because
      // I there implementation causes this to blow up.  The way I see it is this...
      // If he is too far away, I want to move closer and if he is too close I want to move
      // further away.  Given that his coordinates are bigger than mine and he is too close
      // I would want to move away, though in the original algorithm, I would subtract my
      // coordinates from his and eventually add that (with a positive weight) back into me
      // so I would actually get closer to him!
      Point f = new Point(_coordinates, _height);
      f.Subtract(rp);

      double length = f.PlaneNorm();
      while(length < 0.0001) {
        f.Bump();
        length = f.PlaneNorm();
      }
      length = f.Norm();

      double unit = 1.0 / length;

      double weight = 0;

      // if they are both zero, we get a wonderful divide by zero error!
      if(_error > 0 || rp.Error > 0) {
        weight = .1 * (_error / (_error + rp.Error));
      }

      f.ScalarMutliply(unit * latency_distance_diff * weight);

      Add(f);
      CheckHeight();
    }

    public override string ToString()
    {
      StringBuilder sb = new StringBuilder();
      sb.Append("Point (");
      for(int i = 0; i < DEFAULT_DIMENSIONS; i++) {
        sb.Append(_coordinates[i]);
        if(i < DEFAULT_DIMENSIONS - 1) {
          sb.Append(", ");
        }
      }

      sb.Append("), Height: " + _height);
      sb.Append(", Error: " + _error);
      return sb.ToString();
    }
  }

  public class PointTest {
    public void Tester(int nodes)
    {
      List<List<int>> latency;
      ParseDataSet("matrix", out latency);

      VivaldiPoint []points = new VivaldiPoint[nodes];
      for(int i = 0; i < nodes; i++) {
        points[i] = new VivaldiPoint();
      }

      bool done = false;
      int it = 0;
      while(!done && it < 1000) {
        Console.WriteLine("Iteration: " + it++);
        for(int i = 0; i < nodes; i++) {
          for(int j = 0; j < nodes; j++) {
            if(i == j) {
              continue;
            }
            if(latency[i][j] == -1) {
              continue;
            }
            points[i].Process(latency[i][j], points[j]);
          }
        }

        foreach(VivaldiPoint p in points) {
          if(p.Error < .2) {
            done = true;
          } else {
            done = false;
            break;
          }
        }
      }

      int good = 0, bad = 0;
      for(int i = 0; i < nodes; i++) {
        for(int j = 0; j < nodes; j++) {
          if(i == j) {
            continue;
          }
          double al = (double) latency[i][j];
          double cl = points[i].EuclideanDistance(points[j]);
          double diff = Math.Abs((al - cl) / al);
          if (diff > .15) bad++;
          else good++;
        }
      }

      Console.WriteLine("Good: {0}, Bad: {1}", good, bad);
    }

    public void ParseDataSet(string filename, out List<List<int>> data)
    {
      data = new List<List<int>>();
      using(StreamReader fs = new StreamReader(new FileStream(filename, FileMode.Open))) {
        string line = null;
        while((line = fs.ReadLine()) != null) {
          string[] points = line.Split(' ');
          List<int> current = new List<int>(points.Length);
          foreach(string point in points) {
            int val;
            if(!Int32.TryParse(point, out val)) {
              continue;
            }
            current.Add(Int32.Parse(point));
          }
          data.Add(current);
        }
      }
    }

    public static void Main(string[] args)
    {
      PointTest pt = new PointTest();
      pt.Tester(Int32.Parse(args[0]));
    }
  }
}
