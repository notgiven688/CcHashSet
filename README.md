## CcHashSet
---

[Single-file](src/CcHashSet.cs) generic and performant C# implementation of a concurrent (allowing add/remove operations from different threads) HashSet.

### Usage

```csharp
CcHashSet<string> cch = new CcHashSet<string>();

cch.Add("abc");
cch.Add("def");
cch.Add("ghi");
cch.Add("abc");

Console.WriteLine(cch.Count);

cch.Remove("abc");

foreach(string str in cch)
    Console.WriteLine(str);


// outputs:
// 3
// ghi
// def
```

### Benchmark

Intel(R) Xeon(R) CPU E5-4650L 0 @ 2.60GHz\
dotnet --version 5.0.400\
Linux

Adding 4n (int,int)-structs to the hashset and removing 4n (int,int)-structs from the same hashset afterwards.
The structs are composed of pseudo-random numbers in the range of [1, n/1000].

a) In Serial using System.Collections.Generic.HashSet\
b) In Serial using CcHashSet.\
c) In Parallel utilizing 4 Threads using CcHashSet.\
d) In Parallel utilizing 4 Threads using the System.Collections.Concurrent.ConcurrentDictionary.

Times in ms:

<table>
  <thead>
    <tr>
      <th>n</th>
      <th>(a)</th>
      <th>(b)</th>
      <th>(c)</th>
      <th>(d)</th>
    </tr>
  </thead>
  <tbody>
    <tr>
      <td>40,000</td>
      <td>23</td>
      <td>36</td>
      <td>26</td>
      <td>22</td>
    </tr>
    <tr>
      <td>100,000</td>
      <td>45</td>
      <td>85</td>
      <td>47</td>
      <td>47</td>
    </tr>
    <tr>
      <td>3,000,000</td>
      <td>4992</td>
      <td>4979</td>
      <td>1993</td>
      <td>7574</td>
    </tr>
    <tr>
      <td>10,000,000</td>
      <td>18301</td>
      <td>19590</td>
      <td>7716</td>
      <td>47080</td>
    </tr>
  </tbody>
</table>
