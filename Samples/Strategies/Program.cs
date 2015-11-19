﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using LanguageExt;
using LanguageExt.UnitsOfMeasure;
using static LanguageExt.Prelude;
using static LanguageExt.Process;
using static LanguageExt.Strategy;

namespace Strategies
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Test2();
                Test1();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
            }
        }

        static void Test1()
        {
            Func<ProcessId> setup = () =>
                spawn<Unit, string>(
                    Name:  "test1",
                    Setup: () => tellSelf("test 1"),
                    Inbox: (_, msg) =>
                    {
                        Console.WriteLine(msg);
                        failwith<Unit>("fail");
                        return unit;
                    });

            var supervisor = spawn<ProcessId, Unit>(
                Name:     "test1-supervisor",
                Setup:    setup,
                Inbox:    (pid, _) => pid,
                Strategy: OneForOne(
                              Retries(5),
                                  Always(Directive.Restart),
                                  Redirect(
                                  When<Restart>(MessageDirective.ForwardToSelf)))
                );

            Console.WriteLine("Test 1: Press enter when messages stop");
            Console.ReadKey();
        }

        static void Test2()
        {
            int count = 0;

            Func<ProcessId> setup = () =>
                spawn<Unit, string>(
                    Name: "test2",
                    Setup: () =>
                    {
                        if (count < 3)
                        {
                            count++;
                            Console.WriteLine("Setup failure: " + count);
                            failwith<Unit>("setup error");
                        }
                        return unit;
                    },
                    Inbox: (_, msg) =>
                    {
                        Console.WriteLine(msg);
                        return unit;
                    });

            var supervisor = spawn<ProcessId, string>(
                Name: "test2-supervisor",
                Setup: setup,
                Inbox: (pid, _) => {
                    fwd(pid);
                    return pid;
                },
                Strategy: AllForOne(
                              Retries(5),
                                  Match(
                                      With<ProcessSetupException>(Directive.Restart)),
                                  Redirect(
                                      When<Restart>(MessageDirective.ForwardToSelf)))
                );

            tell(supervisor, "Hello");

            Console.WriteLine("Test 2: Press enter when messages stop");
            Console.ReadKey();
        }
    }
}
