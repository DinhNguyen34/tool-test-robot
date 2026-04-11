using System;
using System.Linq;
using System.Reflection;
using VCanPLib;

Console.WriteLine("=== Types ===");
foreach (var t in typeof(VCANPCtrl).Assembly.GetTypes().Where(t => t.FullName != null && t.FullName.Contains("Can")))
{
    Console.WriteLine(t.FullName);
}
Console.WriteLine("=== CanType Names ===");
var canType = typeof(VCANPCtrl).Assembly.GetTypes().FirstOrDefault(t => t.Name == "CanType");
if (canType != null && canType.IsEnum)
{
    foreach (var name in Enum.GetNames(canType)) Console.WriteLine(name);
}
Console.WriteLine("=== CanBaudrate Names ===");
var baud = typeof(VCANPCtrl).Assembly.GetTypes().FirstOrDefault(t => t.Name == "CanBaudrate");
if (baud != null && baud.IsEnum)
{
    foreach (var name in Enum.GetNames(baud)) Console.WriteLine(name);
}
