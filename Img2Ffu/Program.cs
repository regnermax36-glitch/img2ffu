using CommandLine;
using Img2Ffu.Writer;
using System;

namespace Img2Ffu
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            Parser.Default.ParseArguments<Options>(args)
                .WithParsed(o =>
                {
                    Logging.Log("ffu-porter - Creates fully ported FFU from base and port FFU files");
                    Logging.Log("Based on img2ffu by Gustave Monce and Contributors");
                    Logging.Log("Copyright (c) 2019-2024, Gustave Monce - gus33000.me - @gus33000");
                    Logging.Log("Released under the MIT license");
                    Logging.Log("");

                    try
                    {
                        LoggingImplementation logging = new();
                        Porting.CreatePortedFFU(o, logging);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log("Something happened.", LoggingLevel.Error);
                        Logging.Log(ex.ToString(), LoggingLevel.Error);
                        Environment.Exit(1);
                    }
                });
        }
    }
}