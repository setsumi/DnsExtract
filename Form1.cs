using HtmlAgilityPack;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace DnsExtract
{
    public partial class Form1 : Form
    {
        [Flags]
        enum DnsFilter
        {
            None = 0,
            DoH = 1 << 0,
            DoT = 1 << 1,
            DNScrypt = 1 << 2
        }

        enum IPv46 { IPv4 = 0, IPv6 = 1 }

        IPv46 m_ipv46 { get { return radioButton1.Checked ? IPv46.IPv4 : IPv46.IPv6; } }
        string m_iniHeader { get { return m_ipv46 == IPv46.IPv4 ? "[Ipv4_Default]" : "[Ipv6_Default]"; } }

        private int _counter = 0;
        const string _defname = "OpenNIC";
        Shell32.Shell _shell = new Shell32.Shell();
        string _app_dir = AppDomain.CurrentDomain.BaseDirectory;

        public Form1()
        {
            InitializeComponent();

            textBox1.MaxLength = Int32.MaxValue;
            textBox2.MaxLength = Int32.MaxValue;
            textBox5.Text = _defname + "[CNT]=";
            StatusMessage("Ready");
        }

        // Extract DNS addresses from HTML
        private void button1_Click(object sender, EventArgs e)
        {
            _counter = 1; // counter for ParsePadding()

            var filter = DnsFilter.None;
            if (checkBox1.Checked) filter |= DnsFilter.DoH;
            if (checkBox2.Checked) filter |= DnsFilter.DoT;
            if (checkBox3.Checked) filter |= DnsFilter.DNScrypt;

            var doc = new HtmlAgilityPack.HtmlDocument();
            doc.LoadHtml(textBox1.Text);
            HtmlNodeCollection country = doc.DocumentNode.SelectNodes("//div[@id='srvlist']/div");
            if (country == null) return;

            string result = m_iniHeader + Environment.NewLine;
            int cntTotal = 0, cntBlock = 0, /*cntGood = 0,*/ cntFiltered = 0;
            foreach (HtmlNode node in country)
            {
                HtmlNodeCollection server = node.SelectNodes("p");
                foreach (HtmlNode node2 in server)
                {
                    cntTotal++;
                    bool block = false;
                    DnsFilter gotwhat = DnsFilter.None;

                    var n1 = node2.FirstChild;
                    foreach (HtmlNode node3 in n1.ChildNodes)
                    {
                        if (node3.Attributes["title"]?.Value == "Blocklists" || node3.Attributes["title"]?.Value == "Whitelisting")
                        {
                            cntBlock++;
                            block = true;
                            //result += "[BLOCK] ";
                        }
                        else
                        {
                            if (node3.Attributes["title"]?.Value == "DNS over HTTP") gotwhat |= DnsFilter.DoH;
                            if (node3.Attributes["title"]?.Value == "DNS over TLS") gotwhat |= DnsFilter.DoT;
                            if (node3.Attributes["title"]?.Value == "DNScrypt") gotwhat |= DnsFilter.DNScrypt;
                        }
                    }
                    if (!block)
                    {
                        //cntGood++;
                        if ((filter & gotwhat) == filter)
                        {
                            string ipaddr = node2.SelectSingleNode(m_ipv46 == IPv46.IPv4 ?
                                "span[@class='mono ipv4']" : "span[@class='mono ipv6']").InnerHtml;
                            // clean of junk html tags
                            ipaddr = ipaddr.Replace("&nbsp;", "");
                            ipaddr = ipaddr.Replace("<wbr>", "");
                            if (!string.IsNullOrWhiteSpace(ipaddr))
                            {
                                cntFiltered++;
                                string prefix = ParsePadding(textBox5.Text);
                                string suffix = ParsePadding(textBox6.Text);
                                result += prefix;
                                result += ipaddr;
                                result += suffix;
                                result += Environment.NewLine;
                            }
                        }
                    }
                }
            }
            textBox2.Text = result;
            StatusMessage($"Servers total: {cntTotal}, blocking: {cntBlock}, good filtered: {cntFiltered}");


            //====================
            //string text = textBox1.Text;
            //string search = textBox3.Text;
            //MatchCollection matches = Regex.Matches(text, search);
            //string strTotal = "";
            //foreach (Match match in matches)
            //{
            //    string prefix = ParsePadding(textBox5.Text);
            //    string suffix = ParsePadding(textBox6.Text);
            //    strTotal += prefix;

            //    //if (match.Groups.Count > 1) // match groups used in regex
            //    //{
            //    //    for (int i = 1; i < match.Groups.Count; i++)
            //    //    {
            //    //        strTotal += match.Groups[i];
            //    //    }
            //    //}
            //    //else // entire regex match (no groups used)
            //    {
            //        strTotal += match.Value;
            //    }

            //    strTotal += suffix;
            //    strTotal += Environment.NewLine;
            //}
            //textBox2.Text = strTotal;

            //toolStripStatusLabel1.Text = String.Format("there was {0} matches", matches.Count);
        }

        // Generate counter number
        private string ParsePadding(string str)
        {
            string[] result = str.Split(new string[] { "[CNT]" }, StringSplitOptions.RemoveEmptyEntries);
            if (result.Length > 1)
            {
                str = "";
                var last = result.Last();
                foreach (string str2 in result)
                {
                    string s = str2;
                    if (!str2.Equals(last))
                    {
                        s += _counter++.ToString();
                    }
                    str += s;
                }
            }
            return str;
        }

        // Get web page source text
        private void button2_Click(object sender, EventArgs e)
        {
            button2.Enabled = false;
            button2.Invalidate();
            using (WebClient client = new WebClient())
            {
                string html = client.DownloadString(textBox4.Text);
                textBox1.Text = "";
                textBox1.AppendText(Regex.Replace(html, @"\r\n|\n\r|\n|\r", "\r\n")); // normalize line endings and scroll to bottom
            }
            button2.Enabled = true;
            button1.Focus();
        }

        private void Form1_Shown(object sender, EventArgs e)
        {
            button2.Focus();
        }

        // Save extracted list to file
        private void button3_Click(object sender, EventArgs e)
        {
            string savetext = textBox2.Text;
            if (string.IsNullOrWhiteSpace(savetext))
            {
                StatusMessage("Nothing to save");
                return;
            }

            string filename = _app_dir + (m_ipv46 == IPv46.IPv4 ? "DNS_LIST_IPV4.ini" : "DNS_LIST_IPV6.ini");
            File.WriteAllText(filename, savetext, Encoding.GetEncoding(1200)); // UTF-16 LE BOM
            StatusMessage($"Saved: {filename}", "Use list import in DNS Jumper");
        }

        private void button4_Click(object sender, EventArgs e)
        {
            //_shell.Explore(_app_dir);
            var psi = new ProcessStartInfo
            {
                FileName = _app_dir,
                UseShellExecute = true
            };
            Process.Start(psi);
        }

        private void button5_Click(object sender, EventArgs e)
        {
            string filename = _app_dir + "DnsJumper.ini";
            var list = File.ReadAllLines(filename).ToList();
            // delete old servers
            int i = 0;
            bool del = false;
            foreach (string line in list.ToList())
            {
                if (del)
                {
                    if (line.Substring(0, _defname.Length) == _defname)
                    {
                        list.RemoveAt(i);
                        i--;
                    }
                    else
                        break;
                }
                if (line.ToLower() == m_iniHeader.ToLower()) // header found, start deleting
                    del = true;
                i++;
            }
            // insert new servers
            foreach (string line in textBox2.Lines.Reverse())
            {
                if (line.Length > 0 && line.Substring(0, _defname.Length) == _defname)
                {
                    list.Insert(i, line);
                }
            }
            // write file
            File.WriteAllLines(filename, list);
            StatusMessage($"Updated: {filename}", "Run \"Fastest DNS\" test in DNS Jumper");
        }

        private void button6_Click(object sender, EventArgs e)
        {
            _shell.Open(_app_dir + "DnsJumper.exe");
        }

        private void StatusMessage(string msg1, string msg2 = "")
        {
            toolStripStatusLabel1.Text = msg1;
            if (string.IsNullOrEmpty(msg2))
            {
                toolStripStatusLabel2.Text = "";
                toolStripStatusLabel3.Text = "";
            }
            else
            {
                toolStripStatusLabel2.Text = msg2;
                toolStripStatusLabel3.Text = "|";
            }
        }

        private void button7_Click(object sender, EventArgs e)
        {
            if (Clipboard.GetDataObject().GetDataPresent(DataFormats.Text))
            {
                textBox1.Clear();
                textBox1.Paste();
            }
        }

        private void button8_Click(object sender, EventArgs e)
        {
            _shell.Open(textBox4.Text);
        }
    }
}
