﻿using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Drawing;
using System.Threading;
using BitServer.Properties;
using System.Text.RegularExpressions;
using System.IO;

namespace BitServer
{
    public class POP3state
    {
        public bool userOK;
        public bool passOK;
    }

    public class SMTPstate
    {
        public string from = string.Empty;
        public List<string> to = new List<string>();
        public string message = string.Empty;
        public string subject = string.Empty;
        public bool onData = false;
        public bool isCommand = false;
        public bool spam=false;
    }

    public class BitSettings
    {
        public bool Random;
        public bool StripHdr;
        public bool RemQuoting;
        public string IP;
        public int Port;
        public string UName;
        public string UPass;
        public string BitConfig;
        public string Extension;
    }

    class Program
    {

        private const string EXTENSION = "bitmessage.ch";
        private const string RANDOM = "random@";
        private const string COMMAND = "cmd@";
        private const string CONFIG = "BitServer.ini";

        private static POP3server Psrv;
        private static SMTPserver Ssrv;
        
        private static POP3connection POP3;
        private static POP3state P3S;
        
        private static SMTPconnection SMTP;
        private static SMTPstate SMS;
        
        private static POP3message[] POP3msg;
        private static List<POP3message> AuxMessages;
        private static List<string> AckMessage;
        private static BitAddr[] myAddr;
        public static BitSettings BS;

        private static frmLoop GUI;
        private static NotifyIcon NFI;
        private static ContextMenuStrip CMS;
        private static Random R;

        private static Thread tDSN;

        private static bool cont;
        private static int MsgCount = 0;

        [STAThread]
        static void Main(string[] args)
        {
            if (initBS())
            {
                //Check First Run (-1)
                if (BS.Port!=-1 && !BitAPIserver.init(BS))
                {
                    MessageBox.Show(@"Error reading API values.
Check if keys.dat is present and API values are set.
Also verify the API File Path in BitServer.ini is valid.", "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    AuxMessages = new List<POP3message>();
                    AckMessage = new List<string>();

                    tDSN = new Thread(new ThreadStart(checkMSG));
                    tDSN.IsBackground = true;
                    tDSN.Start();

                    if (BS.Port == -1)
                    {
                        Settings_Click(null, null);
                        Application.Run(GUI = new frmLoop());
                    }
                    else
                    {
                        Application.Run(GUI = new frmLoop());
                    }
                }
            }
            else
            {
                var Parts = new INI.INIPart[] { new INI.INIPart(), new INI.INIPart() };
                var NVC = new System.Collections.Specialized.NameValueCollection();

                Parts[0].Section = "API";
                NVC.Add("FILE", "keys.dat");
                NVC.Add("NAME", "AyrA");
                NVC.Add("PASS", "BitMailServer");
                NVC.Add("DEST", "127.0.0.1");
                NVC.Add("PORT", "-1");
                Parts[0].Settings = NVC;

                Parts[1].Section = "MAIL";
                NVC = new System.Collections.Specialized.NameValueCollection();
                NVC.Add("RANDOM", "TRUE");
                NVC.Add("STRIP", "TRUE");
                Parts[1].Settings = NVC;

                INI.RewriteINI(CONFIG, Parts);

                MessageBox.Show("Error reading BitServer.ini Values.\r\nA File with Default Settings was created.\r\nPlease change its settings in the folowing Window.", "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Restart();
            }

            cont = false;

            Console.WriteLine("Exiting...");
            if (NFI != null)
            {
                NFI.Visible = false;
                NFI.Dispose();
                CMS.Dispose();
            }
            tDSN.Join();
        }

        private static void checkMSG()
        {
            if (File.Exists("ack.txt"))
            {
                AckMessage.AddRange(File.ReadAllLines("ack.txt"));
            }
            cont = true;
            while (cont)
            {
                lock (AckMessage)
                {
                    for (int ii = 0; ii < AckMessage.Count; ii++)
                    {
                        string[] parts = AckMessage[ii].Split(new char[]{' '},2);
                        if (parts.Length > 1)
                        {
                            switch (BitAPIserver.BA.getStatus(parts[0]).ToLower().Trim())
                            {
                                case "notfound":
                                    adminMsg("ERR: Error sending message.",
                                        string.Format(@"Your Message '{0}' could not be sent.
Status: notfound
You probably tried to send a message to yourself or from an address that is not yours.
It is also possible, that you deleted the message from the outbox.", parts[1]));
                                    AckMessage.RemoveAt(ii--);
                                    break;
                                case "broadcastsent":
                                    adminMsg("OK: Broadcast sent",
                                        string.Format("Your broadcast '{0}' was sent", parts[1]));
                                    AckMessage.RemoveAt(ii--);
                                    break;
                                case "msgsent":
                                    break;
                                case "ackreceived":
                                    adminMsg("OK: Message sent",
                                        string.Format("Your Message '{0}' was sent and reached its destination.", parts[1]));
                                    AckMessage.RemoveAt(ii--);
                                    break;
                                default:
                                    break;
                            }
                        }
                        else
                        {
                            AckMessage.RemoveAt(ii--);
                        }
                    }
                }
                for (int i = 0; i < 30 && cont; i++)
                {
                    Thread.Sleep(1000);
                }
                if (File.Exists("ack.txt"))
                {
                    File.Delete("ack.txt");
                }
                if (AckMessage.Count > 0)
                {
                    File.WriteAllLines("ack.txt", AckMessage.ToArray());
                }
            }
        }

        internal static void storeBS(BitSettings B)
        {
            INI.setSetting(CONFIG, "API", "FILE", B.BitConfig);
            INI.setSetting(CONFIG, "API", "DEST", B.IP);
            INI.setSetting(CONFIG, "API", "PORT", B.Port.ToString());
            INI.setSetting(CONFIG, "API", "NAME", B.UName);
            INI.setSetting(CONFIG, "API", "PASS", B.UPass);
            INI.setSetting(CONFIG, "MAIL", "RANDOM", B.Random?"TRUE":"FALSE");
            INI.setSetting(CONFIG, "MAIL", "STRIP", B.StripHdr ? "TRUE" : "FALSE");
            INI.setSetting(CONFIG, "MAIL", "RQUOT", B.RemQuoting? "TRUE" : "FALSE");
            INI.setSetting(CONFIG, "MAIL", "EXT", B.Extension);
            initBS();
            BitAPIserver.init(BS);
            bool passIO = true;
            try
            {
                if (BitAPIserver.BA.helloWorld("A", "B") != "A-B")
                {
                    passIO = false;
                    throw new Exception("Wrong Password");
                }
            }
            catch
            {
                string MSG = "Cannot reach the Bitmessage API.";
                if (!passIO)
                {
                    MSG = "Username and Password seem incorrect.";
                }
                if (MessageBox.Show(MSG + "\r\nPlease double check your Settings.\r\nDo so now?",
                    "BitAPI not reached", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                {
                    Settings_Click(null, null);
                }
            }
        }

        private static bool initBS()
        {
            BS = new BitSettings();
            int i = 0;
            BS.BitConfig = INI.getSetting(CONFIG, "API", "FILE");
            if (!string.IsNullOrEmpty(BS.BitConfig) && System.IO.File.Exists(BS.BitConfig))
            {
                BS.IP = INI.getSetting(BS.BitConfig, BitAPIserver.I_SECT, "apiinterface").Trim();
                if (int.TryParse(INI.getSetting(BS.BitConfig, BitAPIserver.I_SECT, "apiport").Trim(), out i))
                {
                    BS.Port = i;
                }
                else
                {
                    return false;
                }
                BS.UName = INI.getSetting(BS.BitConfig, BitAPIserver.I_SECT, "apiusername").Trim();
                BS.UPass = INI.getSetting(BS.BitConfig, BitAPIserver.I_SECT, "apipassword").Trim();
            }
            else
            {
                BS.IP = INI.getSetting(CONFIG, "API", "DEST");
                if (int.TryParse(INI.getSetting(CONFIG, "API", "PORT"), out i))
                {
                    BS.Port = i;
                }
                else
                {
                    return false;
                }
                BS.UName = INI.getSetting(CONFIG, "API", "NAME");
                BS.UPass = INI.getSetting(CONFIG, "API", "PASS");
            }

            BS.Random = toEmpty(INI.getSetting(CONFIG, "MAIL", "RANDOM")).ToUpper() == "TRUE";
            BS.StripHdr = toEmpty(INI.getSetting(CONFIG, "MAIL", "STRIP")).ToUpper() == "TRUE";
            BS.RemQuoting = toEmpty(INI.getSetting(CONFIG, "MAIL", "RQUOT")).ToUpper() == "TRUE";
            BS.Extension = toEmpty(INI.getSetting(CONFIG, "MAIL", "EXT"));
            if (string.IsNullOrEmpty(BS.Extension))
            {
                BS.Extension = EXTENSION;
            }

            return !string.IsNullOrEmpty(BS.IP) &&
                (BS.Port > 0 || BS.Port==-1) &&
                BS.Port <= ushort.MaxValue &&
                !string.IsNullOrEmpty(BS.UName) &&
                !string.IsNullOrEmpty(BS.UPass);
        }

        internal static string toEmpty(string p)
        {
            return string.IsNullOrEmpty(p) ? string.Empty : p;
        }

        internal static void startListener()
        {
            R = new Random();
            Psrv = new POP3server(110);
            Ssrv = new SMTPserver(25);
            Psrv.POP3incomming += new POP3incommingHandler(Psrv_POP3incomming);
            Ssrv.SMTPincomming += new SMTPincommingHandler(Ssrv_SMTPincomming);
        }

        internal static void buildIcon()
        {
            NFI = new NotifyIcon();
            NFI.Icon = Resources.Tray;
            NFI.Text = "BitMail";

            CMS = new ContextMenuStrip();
            CMS.Items.Add(new ToolStripMenuItem("&Settings"));
            CMS.Items.Add(new ToolStripMenuItem("&Exit"));
            CMS.Items[0].Click += new EventHandler(Settings_Click);
            CMS.Items[1].Click += new EventHandler(Exit_Click);
            NFI.ContextMenuStrip = CMS;
            NFI.Visible = true;
            NFI.ShowBalloonTip(5000, "BitMailServer", "BitMailServer is running", ToolTipIcon.Info);
        }

        protected static void Settings_Click(object sender, EventArgs e)
        {
            var f=new frmSettings(BS);
            if (f.ShowDialog() == DialogResult.OK)
            {
                storeBS(f.BS);
            }
            f.Dispose();

        }

        protected static void Exit_Click(object sender, EventArgs e)
        {
            GUI.Close();
        }

        protected static void Ssrv_SMTPincomming(System.Net.Sockets.TcpClient c)
        {
            if (SMTP == null)
            {
                myAddr = Bitmessage.getAddresses(BitAPIserver.BA);
                SMS = new SMTPstate();
                SMTP = new SMTPconnection(c);
                SMTP.SMTPcommand += new SMTPcommandHandler(SMTP_SMTPcommand);
                SMTP.SMTPdropped += new SMTPdroppedHandler(SMTP_SMTPdropped);
                SMTP.msg(220,"WAKE UP BITCH! I'M READY TO KICK ASS, AND IT BETTER BE IMPORTANT");
            }
            else
            {
                SMTPconnection temp = new SMTPconnection(c);
                temp.msg(421, "Try again later you fool!");
                temp.close();
            }
        }

        protected static void SMTP_SMTPdropped()
        {
            SMTP.SMTPdropped -= new SMTPdroppedHandler(SMTP_SMTPdropped);
            SMTP = null;
            Console.WriteLine("DROP");
        }

        protected static void SMTP_SMTPcommand(SMTPconnection SMTP, string command, string[] args, string raw)
        {
            lock (SMTP)
            {
                if (SMS.onData)
                {
                    if (raw == ".")
                    {
                        SMS.onData = false;
                        SMTP.dataMode = false;
                        if (SMS.spam)
                        {
                            SMTP.msg(521, "Someone gets nasty!");
							//spam protection to be implemented
                        }
                        else
                        {
                            if (SMS.isCommand)
                            {
                                switch (SMS.subject.ToLower().Split(' ')[0])
                                {
                                    case "help":
                                        adminMsg("OK: BMS Help", Troll.ADMINHELP);

                                        break;
                                    case "status":
                                        if (SMS.subject.Split(' ').Length > 1)
                                        {
                                            BitAPIserver.BA.statusBar(SMS.subject.Split(new char[] { ' ' }, 2)[1]);
                                            adminMsg("OK: status", "status set to " + SMS.subject.Split(new char[] { ' ' }, 2)[1]);
                                        }
                                        else
                                        {
                                            adminMsg("ERR: status", "cannot set status. You need to specify the value for it");
                                        }
                                        break;
                                    case "killall":
                                        foreach (BitMsg m in Bitmessage.getMessages(BitAPIserver.BA))
                                        {
                                            BitAPIserver.BA.trashMessage(m.msgid);
                                        }
                                        adminMsg("OK: killall", "All messages deleted.");
                                        break;
                                    case "createaddr":
                                        if (SMS.subject.Split(' ').Length == 2)
                                        {
                                            adminMsg("OK: Address generation", Bitmessage.generateAddress(BitAPIserver.BA, SMS.subject.Split(new char[]{' '},2)[1], "B2M generated addr"));
                                        }
                                        else
                                        {
                                            adminMsg("OK: Address generation", Bitmessage.generateAddress(BitAPIserver.BA, "B2M generated addr"));
                                        }
                                        break;
                                    case "listsub":
                                        string sublist = string.Format("{0} {1} Enabled\r\n", size("Address", 38), size("Label", 20));
                                        foreach (Subscription s in Bitmessage.getSubscriptions(BitAPIserver.BA))
                                        {
                                            sublist += string.Format("{0} {1} {2}\r\n",
                                                size(s.address, 38), size(s.label, 20), size(s.enabled.ToString(), 7));
                                        }
                                        adminMsg("OK: Subscription list", sublist);
                                        break;
                                    case "list":
                                        string addrlist = string.Format("{0} {1} Enabled Stream\r\n",
                                            size("Address",38),size("Label",20));
                                        foreach (BitAddr a in Bitmessage.getAddresses(BitAPIserver.BA))
                                        {
                                            addrlist += string.Format("{0} {1} {2} {3}\r\n",
                                                size(a.address,38), size(a.label,20), size(a.enabled.ToString(),7), size(a.stream.ToString(),6));
                                        }
                                        adminMsg("OK: Address list", addrlist);
                                        break;
                                    case "subscribe":
                                    case "sub":
                                        if (SMS.subject.Split(' ').Length >= 2)
                                        {
                                            if (SMS.subject.Split(' ').Length > 2)
                                            {
                                                BitAPIserver.BA.addSubscription(SMS.subject.Split(' ')[1], B64e(SMS.subject.Split(new char[] { ' ' }, 3)[2]));
                                            }
                                            else
                                            {
                                                BitAPIserver.BA.addSubscription(SMS.subject.Split(' ')[1], B64e("--NO NAME--"));
                                            }
                                            adminMsg("OK: subscribe", "subscribed to " + SMS.subject.Split(' ')[1]);
                                        }
                                        else
                                        {
                                            adminMsg("ERR: subscribe", "You need to specify an address");
                                        }
                                        break;
                                    case "unsubscribe":
                                    case "usub":
                                        if (SMS.subject.Split(' ').Length == 2)
                                        {
                                            BitAPIserver.BA.deleteSubscription(SMS.subject.Split(' ')[1]);
                                            adminMsg("OK: unsubscribe", string.Format("Subscription for {0} deleted",SMS.subject.Split(' ')[1]));
                                        }
                                        else
                                        {
                                            adminMsg("ERR: unsubscribe", "You need to specify an address");
                                        }
                                        break;
                                    default:
                                        adminMsg("ERR: unknown command", string.Format(@"The command you used is not valid.
To get a list of valid commands, use the 'help' command.
Your command line: {0}",SMS.subject));
                                        break;
                                }
                            }
                            else
                            {
                                if (BS.StripHdr)
                                {
                                    SMS.message = FilterHeaders(SMS.message);
                                }
                                foreach (string s in SMS.to)
                                {
                                    if (s.ToUpper() == "BROADCAST")
                                    {
                                        AckMessage.Add(BitAPIserver.BA.sendBroadcast(SMS.from, B64e(SMS.subject), B64e(SMS.message)));
                                    }
                                    else
                                    {
                                        AckMessage.Add(BitAPIserver.BA.sendMessage(s, SMS.from, B64e(SMS.subject), B64e(SMS.message)));
                                    }
                                }
                            }
                            SMTP.msg(250, "This better not be spam!");
                        }
                        SMS = new SMTPstate();
                    }
                    else
                    {
                        if (raw.ToLower().StartsWith("subject: ") && string.IsNullOrEmpty(SMS.subject))
                        {
                            if (BS.StripHdr)
                            {
                                SMS.subject = stripQuoted(raw.Substring(9).Trim());
                            }
                            else
                            {
                                SMS.subject = raw.Substring(9).Trim();
                            }
                        }
                        //MIME from field, if the address exists set this.
                        if (raw.ToLower().StartsWith("from: "))
                        {
                            string fromAddr=getAddress(raw);
                            if (!string.IsNullOrEmpty(fromAddr))
                            {
                                foreach (BitAddr a in myAddr)
                                {
                                    if (a.address.ToLower() == fromAddr.ToLower())
                                    {
                                        SMS.from = a.address;
                                    }
                                }
                            }
                        }
                        if (BS.StripHdr)
                        {
                            SMS.message += stripQuoted(raw + "\r\n");
                        }
                        else
                        {
                            SMS.message += raw + "\r\n";
                        }
                    }
                }
                else
                {
                    switch (command)
                    {
                        case "EHLO":
                        case "HELO":
                            SMTP.msg(250, "I am busy stealing your secrets, it better be important");
                            break;
                        case "MAIL":
                            if (args.Length > 0 && args[0].ToUpper().StartsWith("FROM:"))
                            {
                                string addr;
                                if (!string.IsNullOrEmpty(addr = check(raw)))
                                {
                                    if (addr == RANDOM.Split('@')[0] && BS.Random)
                                    {
                                        SMS.from=Bitmessage.generateAddress(BitAPIserver.BA, "RANDOM");
                                        SMTP.msg(250, "Generated your address: "+SMS.from);
                                    }
                                    else
                                    {
                                        bool found = false;
                                        foreach (var Addr in myAddr)
                                        {
                                            if (Addr.address.ToLower() == addr.ToLower() || Addr.label.ToLower() == addr.ToLower())
                                            {
                                                SMS.from = Addr.address;
                                                found = true;
                                            }
                                        }
                                        if (found)
                                        {
                                            SMTP.msg(250, "At least it is your address");
                                        }
                                        else
                                        {
                                            SMTP.msg(530, "Fuckoff");
                                        }
                                    }
                                }
                                else
                                {
                                    SMTP.msg(500, "HOW DUMB ARE YOU? THIS IS NOT EVEN A CORRECT BITMESSAGE ADDRESS...");
                                }

                            }
                            else
                            {
                                SMTP.msg(500, "WRONG!!!!");
                            }
                            break;
                        case "RCPT":
                            if (args.Length > 0 && args[0].ToUpper().StartsWith("TO:"))
                            {
                                string addr = check(raw);
                                if (!string.IsNullOrEmpty(addr))
                                {
                                    if (addr == COMMAND.Split('@')[0])
                                    {
                                        SMS.isCommand = true;
                                        SMTP.msg(250, "Give me the damn subject line already");
                                    }
                                    else
                                    {
                                        SMS.to.Add(addr);
                                        SMTP.msg(250, "I added the address");
                                    }
                                }
                                else
                                {
                                    SMTP.msg(551, "TRY A FUCKING EXTERNAL SERVER OR USE <BITMESSAGE.CH>");
                                }

                            }
                            else
                            {
                                SMTP.msg(500, "WRONG!!!!");
                            }
                            break;
                        case "DATA":
                            if (!string.IsNullOrEmpty(SMS.from) && (SMS.to.Count > 0 || SMS.isCommand))
                            {
                                SMS.onData = true;
                                SMTP.dataMode = true;
                                //As a Mail Server we add this
                                SMS.message = "Return-Path: <" + SMS.from + "@bitmessage.ch>\r\n";
                                SMTP.msg(354, "I am waiting...");
                            }
                            else
                            {
                                SMTP.msg(500, "DEFINE MAIL AND RCPT YOU IDIOT");
                            }
                            break;
                        case "HELP":
                            SMTP.msg(241, "THERE IS NO FUCKING HELP FOR YOUR PROBLEM. JUST LIVE WITH IT!");
                            break;
                        case "NOOP":
                            SMTP.msg(Troll.Trollface);
                            break;
                        case "QUIT":
                            if (string.IsNullOrEmpty(SMS.from) || SMS.to.Count == 0)
                            {
                                SMTP.msg(221, "NEXT TIME SEND A MAIL YOU STOOPID BITCH");
                            }
                            else
                            {
                                SMTP.msg(221, "I relay your message, but only because it's you!");
                            }
                            SMTP.close();
                            break;
                        case "RSET":
                            SMS = new SMTPstate();
                            SMTP.msg(250, new string[] { "Who is too stupid to write a message without errors?", "You are too stupid to write a message!" });
                            break;
                        case "VRFY":
                        case "EXPN":
                        case "TURN":
                        case "AUTH":
                            SMTP.msg(502, "I HAVE NO FUCKING TIME FOR THIS. SCREW THIS COMMAND");
                            break;
                        default:
                            SMTP.msg(500, "STOP DOING THIS!");
                            break;
                    }
                }
            }
        }

        private static string getAddress(string raw)
        {
            if (raw.Contains(":"))
            {
                raw = raw.Substring(raw.IndexOf(':'));
            }
            if (raw.Contains("<"))
            {
                raw = raw.Substring(raw.IndexOf('<') + 1);
            }
            if (raw.Contains(">"))
            {
                raw = raw.Substring(0,raw.IndexOf('>'));
            }
            return raw.Split('@')[0].Trim();
        }

        private static void adminMsg(string subject, string body)
        {
            BitAddr a = new BitAddr();
            a.address = "admin";
            a.enabled = true;
            a.label = "local admin";
            a.stream = 1;

            BitMsg BM = new BitMsg();
            BM.encodingType = 3;
            BM.fromAddress = "cmd";
            BM.toAddress = "admin";
            BM.message = body;
            BM.subject = subject;
            BM.receivedTime = UnixTime.ConvertTo(DateTime.Now);
            BM.msgid = MsgCount.ToString();
            MsgCount++;
            AuxMessages.Add(new POP3message(0, BM, new BitAddr[] { a }));
        }

        private static string stripQuoted(string input)
        {
            var occurences = new Regex(@"=[0-9A-F]{2}", RegexOptions.Multiline);
            var matches = occurences.Matches(input);
            foreach (Match match in matches)
            {
                char hexChar = (char)Convert.ToInt32(match.Groups[0].Value.Substring(1), 16);
                input = input.Replace(match.Groups[0].Value, hexChar.ToString());
            }
            return input.Replace("=\r\n", "").Replace("= \r\n", "");
        }

        protected static string B64e(string p)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(p), Base64FormattingOptions.None);
        }

        protected static string B64d(string p)
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(p));
        }

        protected static string check(string raw)
        {
            if (raw.Contains(":"))
            {
                raw = raw.Split(':')[1].Trim();
            }
            if (raw.Contains("<") && raw.Contains(">"))
            {
                raw = raw.Substring(raw.LastIndexOf('<') + 1);
                raw = raw.Substring(0, raw.IndexOf('>'));
            }
            if (raw.ToLower().EndsWith("@" + EXTENSION))
            {
                return raw.Substring(0, raw.IndexOf('@'));
            }
            return null;
        }

        protected static void Psrv_POP3incomming(System.Net.Sockets.TcpClient c)
        {
            if (POP3 == null)
            {
                P3S = new POP3state();
                P3S.passOK = P3S.userOK = false;
                POP3 = new POP3connection(c);
                POP3.POP3command += new POP3commandHandler(POP3_POP3command);
                POP3.POP3dropped += new POP3droppedHandler(POP3_POP3dropped);
                POP3.ok("WAKE UP BITCH! I'M READY TO KICK ASS, AND IT BETTER BE IMPORTANT");
            }
            else
            {
                new POP3connection(c).err("Try again later you fool!", true);
            }
        }

        protected static void POP3_POP3dropped()
        {
            POP3 = null;
            Console.WriteLine("DROP");
        }

        protected static void POP3_POP3command(POP3connection POP3, string command, string[] args, string raw)
        {
            lock (POP3)
            {
                Console.WriteLine(raw);
                switch (command)
                {
                    case "USER":
                        if (P3S.userOK || P3S.passOK)
                        {
                            POP3.err("YOU ALREADY SET THE USERNAME STUPID BITCH!", false);
                        }
                        else
                        {
                            P3S.userOK = true;
                            POP3.ok("Obviously you need to send the password now");
                        }
                        break;
                    case "PASS":
                        if (P3S.userOK)
                        {
                            if (P3S.passOK)
                            {
                                POP3.err("WE FUCKING DID THIS ALREADY!", false);
                            }
                            else
                            {
                                P3S.passOK = true;
                                var allMsg = Bitmessage.getMessages(BitAPIserver.BA);
                                if (allMsg == null)
                                {
                                    NFI.ShowBalloonTip(5000, "API Error", "POP3: Cannot retreive Messages.\r\nVerify Bitmessage runs and the API Parameters are correct", ToolTipIcon.Error);
                                    POP3.err("CONFIGURE ME PROPERLY!",true);
                                    break;
                                }
                                else
                                {
                                    myAddr=Bitmessage.getAddresses(BitAPIserver.BA);
                                    POP3msg = new POP3message[allMsg.Length];
                                    for (int i = 0; i < POP3msg.Length; i++)
                                    {
                                        POP3msg[i] = new POP3message(i + 1, allMsg[i],myAddr);
                                    }
                                    POP3.ok("Thanks. next time do it faster");
                                }
                            }
                        }
                        else
                        {
                            POP3.err("I NEED YOUR NAME ASSHOLE!", false);
                        }
                        break;
                    case "CAPA":
                        POP3.sendRaw(@"+OK I don't know why I am so nice to you...
USER
PASS
TOP
STAT
RETR
UIDL
NOOP
AYRA
CAPA
LIST
DELE
HELP
RSET
QUIT
.
");
                        break;
                    case "STAT":
                        if (P3S.passOK && P3S.userOK)
                        {
                            long size = 0;
                            int count = 0;
                            int i = 0;
                            for (i = 0; i < POP3msg.Length; i++)
                            {
                                if (!POP3msg[i].Deleted)
                                {
                                    count++;
                                    size += POP3msg[i].Body.Length;
                                }
                            }
                            for (i = 0; i < AuxMessages.Count; i++)
                            {
                                if (!AuxMessages[i].Deleted)
                                {
                                    count++;
                                    size += AuxMessages[i].Body.Length;
                                }
                            }
                            POP3.ok(string.Format("{0} {1}", POP3msg.Length+AuxMessages.Count, size));
                        }
                        break;
                    case "LIST":
                        if (P3S.passOK && P3S.userOK)
                        {
                            POP3.ok("INCOMMING MAILS! BITCH");
                            int i = 0;
                            int j = 0;
                            for (i = 0; i < POP3msg.Length; i++)
                            {
                                if (!POP3msg[i].Deleted)
                                {
                                    POP3.sendRaw(string.Format("{0} {1}\r\n", i + 1, POP3msg[i].Body.Length));
                                }
                                j++;
                            }
                            for (i = 0; i < AuxMessages.Count; i++)
                            {
                                if (!AuxMessages[i].Deleted)
                                {
                                    POP3.sendRaw(string.Format("{0} {1}\r\n", j + 1, AuxMessages[i].Body.Length));
                                }
                                j++;
                            }
                            POP3.sendRaw(".\r\n");
                        }
                        else
                        {
                            POP3.err("LOGIN FIRST YOU SON OF A BITCH", false);
                        }
                        break;
                    case "TOP":
                        if (P3S.passOK && P3S.userOK)
                        {
                            if (args.Length >= 1)
                            {
                                int linesHeader = 0;
                                int i = 0;

                                if (args.Length == 2 && !int.TryParse(args[1], out linesHeader))
                                {
                                    POP3.err("WRONG ARGUMENT ASSHOLE!", false);
                                    return;
                                }

                                if (int.TryParse(args[0], out i) && i > 0 && i <= POP3msg.Length && !POP3msg[i - 1].Deleted)
                                {
                                    POP3.ok("listen carefully!");
                                    bool countlines = false;
                                    foreach (string s in POP3msg[i - 1].Body.Split('\n'))
                                    {
                                        if (s.Trim() == string.Empty && !countlines)
                                        {
                                            countlines = true;
                                            POP3.sendRaw("\r\n");
                                        }
                                        else
                                        {
                                            if (!countlines)
                                            {
                                                POP3.sendRaw(s.TrimEnd() + "\r\n");
                                            }
                                            else if (linesHeader > 0)
                                            {
                                                linesHeader--;
                                                POP3.sendRaw(s.TrimEnd() + "\r\n");
                                            }
                                        }
                                    }
                                    POP3.sendRaw("\r\n.\r\n");
                                }
                                else if (int.TryParse(args[0], out i) && i > 0 && i-POP3msg.Length <= AuxMessages.Count)
                                {
                                    i -= POP3msg.Length;
                                    POP3.ok("listen carefully!");
                                    bool countlines = false;
                                    foreach (string s in AuxMessages[i - 1].Body.Split('\n'))
                                    {
                                        if (s.Trim() == string.Empty && !countlines)
                                        {
                                            countlines = true;
                                            POP3.sendRaw("\r\n");
                                        }
                                        else
                                        {
                                            if (!countlines)
                                            {
                                                POP3.sendRaw(s.TrimEnd() + "\r\n");
                                            }
                                            else if (linesHeader > 0)
                                            {
                                                linesHeader--;
                                                POP3.sendRaw(s.TrimEnd() + "\r\n");
                                            }
                                        }
                                    }
                                    POP3.sendRaw("\r\n.\r\n");
                                }
                                else
                                {
                                    POP3.err("I DO NOT HAVE YOUR STUFF!", false);
                                }
                            }
                            else
                            {
                                POP3.err("LEARN COMMANDS BITCH!", false);
                            }
                        }
                        else
                        {
                            POP3.err("LOGIN FIRST YOU SON OF A BITCH", false);
                        }
                        break;
                    case "DELE":
                        if (P3S.passOK && P3S.userOK)
                        {
                            int ID = 0;
                            if (args.Length == 1 && int.TryParse(args[0], out ID) && ID > 0 && ID <= POP3msg.Length && !POP3msg[ID - 1].Deleted)
                            {
                                POP3.ok("I will take care of it!");
                                POP3msg[ID - 1].MarkDelete();
                            }
                            else if(int.TryParse(args[0], out ID))
                            {
                                //shifts the Message ID
                                ID -= POP3msg.Length;
                                if (ID <= AuxMessages.Count)
                                {
                                    for (int i = 0; i < AuxMessages.Count; i++)
                                    {
                                        POP3.ok("I will take care of it!");
                                        AuxMessages[ID - 1].MarkDelete();
                                    }
                                }
                                else
                                {
                                    POP3.err("I DO NOT HAVE YOUR STUFF!", false);
                                }
                            }
                        }
                        else
                        {
                            POP3.err("LOGIN FIRST YOU SON OF A BITCH", false);
                        }
                        break;
                    case "HELP":
                        POP3.ok("WHAT THE FUCK YOU WANT? see RFC 1939 you douchebag");
                        break;
                    case "AYRA":
                        POP3.ok("I still hate you");
                        break;
                    case "RETR":
                        if (P3S.passOK && P3S.userOK)
                        {
                            int ID = 0;
                            if (args.Length == 1 && int.TryParse(args[0], out ID) && ID > 0 && ID <= POP3msg.Length && !POP3msg[ID - 1].Deleted)
                            {
                                POP3.ok("listen carefully!");
                                POP3.sendRaw(POP3msg[ID - 1].Body + "\r\n.\r\n");
                            }
                            else if (args.Length == 1 && int.TryParse(args[0], out ID) && ID > 0 && ID <= POP3msg.Length + AuxMessages.Count && !AuxMessages[ID - 1 - (POP3msg.Length)].Deleted)
                            {
                                ID -= POP3msg.Length;
                                POP3.ok("listen carefully!");
                                POP3.sendRaw(AuxMessages[ID - 1].Body + "\r\n.\r\n");
                            }
                            else
                            {
                                POP3.err("I DO NOT HAVE YOUR STUFF!", false);
                            }
                        }
                        else
                        {
                            POP3.err("LOGIN FIRST YOU SON OF A BITCH", false);
                        }
                        break;
                    case "QUIT":
                        POP3.ok("It's about time you go already");
                        if (POP3msg != null)
                        {
                            foreach (var P in POP3msg)
                            {
                                if (P.Deleted)
                                {
                                    BitAPIserver.BA.trashMessage(P.UID);
                                }
                            }
                            for (int i = 0; i < AuxMessages.Count; i++)
                            {
                                if (AuxMessages[i].Deleted)
                                {
                                    AuxMessages.RemoveAt(i--);
                                }
                            }
                        }
                        POP3.close();
                        break;
                    case "NOOP":
                        POP3.ok("Bitch Please, stop it!");
                        break;
                    case "RSET":
                        if (P3S.passOK && P3S.userOK)
                        {
                            foreach (var P in POP3msg)
                            {
                                if (P.Deleted)
                                {
                                    P.Reset();
                                }
                            }
                            for (int i = 0; i < AuxMessages.Count; i++)
                            {
                                AuxMessages[i].Reset();
                            }
                            POP3.ok("Don't make mistakes in the future!");
                        }
                        else
                        {
                            POP3.err("LOGIN FIRST YOU SON OF A BITCH", false);
                        }
                        break;
                    case "UIDL":
                        if (P3S.passOK && P3S.userOK)
                        {
                            int ID = 0;
                            if (args.Length == 1 && int.TryParse(args[0], out ID) && ID > 0 && ID <= POP3msg.Length)
                            {
                                POP3.ok(string.Format("{0} {1}", ID, POP3msg[ID - 1].UID));
                            }
                            else
                            {
                                POP3.ok("Here you go!");
                                int i = 0;
                                int j = 0;
                                for (i = 0; i < POP3msg.Length; i++)
                                {
                                    j++;
                                    POP3.sendRaw(string.Format("{0} {1}\r\n", j, POP3msg[i].UID));
                                }
                                for (i = 0; i < AuxMessages.Count; i++)
                                {
                                    j++;
                                    POP3.sendRaw(string.Format("{0} {1}\r\n", j, AuxMessages[i].UID));
                                }
                                POP3.sendRaw(".\r\n");
                            }
                        }
                        else
                        {
                            POP3.err("LOGIN FIRST YOU SON OF A BITCH", false);
                        }
                        break;
                    default:
                        POP3.err("Watch your language!", false);
                        break;
                }
            }
        }

        protected static string FilterHeaders(string p)
        {
            string retValue = string.Empty;
            bool headerMode = true;

            string[] Lines = p.Split(new string[] { "\r\n" }, StringSplitOptions.None);

            foreach (string Line in Lines)
            {
                if (headerMode)
                {
                    if (!Line.Contains(":") && !Line.StartsWith("\t") && !Line.StartsWith(" "))
                    {
                        if (BS.RemQuoting)
                        {
                            retValue += Line.TrimStart(new char[] { '>' }) + "\r\n";
                        }
                        else
                        {
                            retValue += Line + "\r\n";
                        }
                    }
                    headerMode = (Line.Trim() != String.Empty);
                }
                else
                {

                    if (BS.RemQuoting)
                    {
                        retValue += Line.TrimStart(new char[] { '>' }) + "\r\n";
                    }
                    else
                    {
                        retValue += Line + "\r\n";
                    }
                    if (Line.StartsWith("---") && Line.EndsWith("---"))
                    {
                        headerMode = true;
                    }
                }
            }

            /*
            //splitter between header and content
            if (p.Contains("\r\n\r\n"))
            {
                p=p.Substring(p.IndexOf("\r\n\r\n") + 4);
            }
            */
            return retValue;
        }

        private static string size(string s, int l)
        {
            if (s.Length > l)
            {
                if (l > 3)
                {
                    s=s.Substring(0, l - 3) + "...";
                }
                else
                {
                    s=s.Substring(0, l);
                }
            }
            else if (s.Length<l)
            {
                while (s.Length < l)
                {
                    s += " ";
                }
            }
            return s;
        }
    }
}
