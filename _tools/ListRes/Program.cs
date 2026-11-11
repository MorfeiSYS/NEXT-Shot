using System;
using System.IO;
using System.Reflection;
class R {
  static void Main() {
    var asm = Assembly.LoadFrom(@"b:\CODE\NEXT-Shot\Skrinshoter.exe");
  foreach (var n in asm.GetManifestResourceNames())
    Console.WriteLine(n);
}
}
