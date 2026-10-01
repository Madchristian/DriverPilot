using DriverPilot.Tests;

try
{
    Console.WriteLine(ReportTests.Run());
    Console.WriteLine(ResponseTests.Run());
    Console.WriteLine(await SessionTests.Run());
    return 0;
}
catch (Exception ex) { Console.WriteLine(ex); return 1; }
