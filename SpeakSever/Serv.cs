﻿using MySql.Data.MySqlClient;
using MySql.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SpeakSever
{
    class Serv
    {
        //监听嵌套字
        public Socket listenfd;
        //客户端连接
        public Conn[] conns;
        //最大连接数
        public int maxConn = 50;

        MySqlConnection sqlconn;

        //获取连接池索引，返回负数表示获取失败
        public int NewIndex()
        {
            if (conns == null)
                return -1;
            for (int i = 0; i < conns.Length; i++)
            {
                if (conns[i] == null)
                {
                    conns[i] = new Conn();
                    return i;
                }
                else if (conns[i].isUse == false)
                {
                    return i;
                }
            }
            return -1;
        }

        //开启服务器
        public void Start(string host, int port)
        {
            //数据库
            string connStr = "Database=msgboard; Data Source=rm-uf679u840smz34fcxfo.mysql.rds.aliyuncs.com;";
            connStr += "User Id=root; Password=Azazaz12; port=3306";
            sqlconn = new MySqlConnection(connStr);
            try
            {
                sqlconn.Open();
            }
            catch (Exception e)
            {
                Console.Write("[数据库]连接失败" + e.Message);
                return;
            }

            //连接池
            conns = new Conn[maxConn];
            for (int i = 0; i < maxConn; i++)
            {
                conns[i] = new Conn();
            }

            //socket
            listenfd = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            //Bind
            IPAddress ipAdr = IPAddress.Parse(host);
            IPEndPoint ipEp = new IPEndPoint(ipAdr, port);
            listenfd.Bind(ipEp);
            //listen
            listenfd.Listen(maxConn);
            //accept
            listenfd.BeginAccept(AcceptCb, null);
            Console.WriteLine("[服务器]启动成功");


        }
        /// <summary>
        /// Accept回调
        /// </summary>
        /// <param name="ar"></param>
        private void AcceptCb(IAsyncResult ar)
        {
            try
            {
                Socket socket = listenfd.EndAccept(ar);
                int index = NewIndex();

                if (index < 0)
                {
                    socket.Close();
                    Console.WriteLine("警告连接池已满");
                }
                else
                {
                    Conn conn = conns[index];
                    conn.Init(socket);
                    string adr = conn.GetAdress();
                    Console.WriteLine("客户端连接[" + adr + "] conn池id:" + index);
                    conn.socket.BeginReceive(conn.readBuff, conn.buffCount, conn.BuffRemain(), SocketFlags.None, ReceiveCb, conn);
                    listenfd.BeginAccept(AcceptCb, null);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("AcceptCb失败:" + e.Message);

            }
        }


        private void ReceiveCb(IAsyncResult ar)
        {
            Conn conn = (Conn)ar.AsyncState;
            try
            {
                int count = conn.socket.EndReceive(ar);
                //关闭信号
                if (count <= 0)
                {
                    Console.WriteLine("收到[" + conn.GetAdress() + "]断开连接");
                    conn.Close();
                    return;
                }
                //数据处理
                string str = System.Text.Encoding.UTF8.GetString(conn.readBuff, 0, count);
                Console.WriteLine("收到 [" + conn.GetAdress() + "] 数据" + str);
                HandleMsg(conn, str);
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(str);                
                //广播
                for (int i=0; i < conns.Length; i++)
                {
                    if (conns[i] == null)
                        continue;
                    if (!conns[i].isUse)
                        continue;
                    Console.WriteLine("将消息转播给" + conns[i].GetAdress());
                    conns[i].socket.Send(bytes);
                }
                //继续接收
                conn.socket.BeginReceive(conn.readBuff, conn.buffCount, conn.BuffRemain(), SocketFlags.None, ReceiveCb, conn);

            }
            catch (Exception e)
            {
                Console.WriteLine("收到 [" + conn.GetAdress() + "] 断开连接");
                conn.Close();
            }


        }

        private void HandleMsg(Conn conn, string str)
        {
            //获取数据
            if(str.Contains("_GET"))
            {
                string cmdStr = "select * from msg order by id asc limit 10;";
                MySqlCommand cmd = new MySqlCommand(cmdStr, sqlconn);
                try
                {
                    MySqlDataReader dataReader = cmd.ExecuteReader();
                    str = "";
                    while (dataReader.Read())
                    {
                        str += dataReader["name"] + ":" + dataReader["msg"] + "\n\r";
                    }
                    dataReader.Close();
                    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(str);
                    conn.socket.Send(bytes);
                }
                catch(Exception e)
                {
                    Console.WriteLine("[数据库]查询失败" + e.Message);
                }
            }
            //插入数据
            else
            {
                string cmdStrFormat = "insert into msg set name = '{0}' ,msg = '{1}';";
                string cmdStr = string.Format(cmdStrFormat, conn.GetAdress(), str);
                MySqlCommand cmd = new MySqlCommand(cmdStr, sqlconn);
                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch(Exception e)
                {
                    Console.WriteLine("[数据库]插入失败" + e.Message);
                }


            }

        }
    }
}
