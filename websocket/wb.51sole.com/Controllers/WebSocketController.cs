using _51Sole.DJG.Common;
using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Results;
using System.Web.WebSockets;
using wb._51sole.com.Models;

namespace wb._51sole.com.Controllers
{
    /// <summary>
    ///  WebSocket通讯
    /// </summary>
    [RoutePrefix("WebSocket")]
    [AllowAnonymous]
    public class WebSocketController : ApiController
    {
        /// Socket 集合 对应 用户ID 与 WebSocket对象
        /// </summary>
        public static ConcurrentDictionary<string, SocketInfo> socket_list = new ConcurrentDictionary<string, SocketInfo>();
        /// <summary>
        /// get方法，判断是否是websocket请求
        /// </summary>
        /// <returns></returns>
        [Route("")]
        public HttpResponseMessage Get()
        {
            if (HttpContext.Current.IsWebSocketRequest)
            {
                HttpContext.Current.AcceptWebSocketRequest(ProcessWebsocket);
            }
            return new HttpResponseMessage(HttpStatusCode.SwitchingProtocols);
        }
        /// <summary>
        /// 异步线程处理websocket消息
        /// </summary>
        /// <param name="arg"></param>
        /// <returns></returns>
        private async Task ProcessWebsocket(AspNetWebSocketContext arg)
        {
            var ipAddress = arg.UserHostAddress;
            var ua = arg.UserAgent;
            WebSocket socket = arg.WebSocket;
            while (true)
            {
                string userid = string.Empty;
                try
                {
                    ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[1024 * 16]);
                    string returnMessage = "";//返回消息             
                    WebSocketReceiveResult result = null;
                    var timeOut = new CancellationTokenSource(15000).Token;
                    try
                    {
                        result = await socket.ReceiveAsync(buffer, timeOut);
                    }
                    catch
                    {
                        if (socket.State == WebSocketState.Aborted || socket.State == WebSocketState.Closed)
                        {
                            //释放websoket对象
                            var l = socket_list.Where(t => t.Value.socket == socket).FirstOrDefault();
                            if (!string.IsNullOrEmpty(l.Key))
                            {
                                SocketInfo ws = null;
                                userid = l.Key;
                                socket_list.TryRemove(l.Key, out ws);
                            }
                        }
                        break;
                    }
                    if (result != null)
                    {
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            //释放websoket对象
                            var l = socket_list.Where(t => t.Value.socket == socket);
                            if (l.Count() > 0)
                            {
                                foreach (var item in l)
                                {
                                    SocketInfo ws = null;
                                    userid = item.Key;
                                    socket_list.TryRemove(item.Key, out ws);
                                }
                            }
                            try
                            {
                                await socket.CloseOutputAsync(WebSocketCloseStatus.Empty, string.Empty, CancellationToken.None);//如果client发起close请求，对client进行ack   
                            }
                            catch (Exception ex)
                            {
                                Logger.WriteLog("关闭WebSocket异常:" + ex.Message);
                            }
                            break;
                        }
                        if (socket.State == WebSocketState.Connecting)
                        {

                        }
                        else if (socket.State == WebSocketState.Open)
                        {
                            try
                            {
                                string message = Encoding.UTF8.GetString(buffer.Array, 0, result.Count);
                                WetSocketRequestModel model = JsonConvert.DeserializeObject<WetSocketRequestModel>(message);
                                if (model.type == "open_connect")
                                {
                                    string uid = model.uid;
                                    string sourcePlatform = model.SourePlatform;
                                    //判断用户ID是否为空，为空则初始化ID
                                    if (string.IsNullOrEmpty(model.uid))
                                    {
                                        uid = Guid.NewGuid().ToString().Replace("-", "");
                                        socket_list.TryAdd(uid, new SocketInfo
                                        {
                                            socket = socket,
                                            user = new UserInfo
                                            {
                                                uid = uid,
                                                sourcePlatform = sourcePlatform,
                                                userAgent = ua,
                                                IpAddress = ipAddress,
                                                onlineTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                                            }
                                        });
                                    }
                                    else
                                    {
                                        if (socket_list.Where(t => t.Key == model.uid).Count() == 0)
                                        {
                                            socket_list.TryAdd(uid, new SocketInfo
                                            {
                                                socket = socket,
                                                user = new UserInfo
                                                {
                                                    uid = uid,
                                                    sourcePlatform = sourcePlatform,
                                                    userAgent = ua,
                                                    IpAddress = ipAddress,
                                                    onlineTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                                                }
                                            });
                                        }
                                        else
                                        {
                                            //更新登录信息
                                            if (socket_list[model.uid].socket != socket)
                                            {
                                                //关闭之前的链接,重置为当前链接
                                                try
                                                {
                                                    SocketInfo ws = null;
                                                    bool isSuccess = socket_list.TryGetValue(model.uid, out ws);
                                                    if (isSuccess)
                                                    {
                                                        if (ws != null && ws.socket != null && ws.socket.State == WebSocketState.Open)
                                                        {
                                                            if (model.SourePlatform != "web")
                                                            {
                                                                returnMessage = "{\"type\":\"force_exit\",\"uid\":\"" + model.uid + "\",\"message\":\"你在其他设备登录了!\"}";
                                                                buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(returnMessage));
                                                                await ws.socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                                            }
                                                            await ws.socket.CloseOutputAsync(WebSocketCloseStatus.Empty, string.Empty, CancellationToken.None);//主动关闭连接
                                                        }
                                                    }
                                                    socket_list[model.uid] = new SocketInfo
                                                    {
                                                        socket = socket,
                                                        user = new UserInfo
                                                        {
                                                            uid = uid,
                                                            sourcePlatform = sourcePlatform,
                                                            userAgent = ua,
                                                            IpAddress = ipAddress,
                                                            onlineTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                                                        }
                                                    };
                                                }
                                                catch (Exception ex)
                                                {
                                                    Logger.WriteLog("重置用户" + model.uid + "链接异常:" + ex.Message);
                                                }
                                            }
                                            else
                                            {
                                                socket_list[model.uid] = new SocketInfo
                                                {
                                                    socket = socket,
                                                    user = new UserInfo
                                                    {
                                                        uid = uid,
                                                        sourcePlatform = sourcePlatform,
                                                        userAgent = ua,
                                                        IpAddress = ipAddress,
                                                        onlineTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                                                    }
                                                };
                                            }
                                        }
                                    }
                                    userid = uid;
                                    //建立连接                  
                                    returnMessage = "{\"type\":\"open_init\",\"uid\":\"" + uid + "\"}";
                                    buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(returnMessage));
                                    await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                    //心跳连接
                                    returnMessage = "{\"type\":\"open_health\",\"uid\":\"" + model.uid + "\",\"message\":\"连接成功!\"}";
                                    buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(returnMessage));
                                    await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                }
                                else if (model.type == "send_msg")
                                {
                                    if (!socket_list.ContainsKey(model.uid))
                                    {
                                        returnMessage = "{\"type\":\"open_err\",\"uid\":\"\",\"message\":\"非法连接!\"}";
                                        buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(returnMessage));
                                        await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                    }
                                    else
                                    {
                                        //转发消息
                                        if (socket_list.ContainsKey(model.touid ?? ""))
                                        {
                                            try
                                            {
                                                WebSocket desSocket = socket_list[model.touid].socket;
                                                if (desSocket != null && desSocket.State == WebSocketState.Open)
                                                {
                                                    returnMessage = JsonConvert.SerializeObject(new WetSocketRequestModel
                                                    {
                                                        type = "get_msg",
                                                        uid = model.uid,
                                                        touid = model.touid,
                                                        SourePlatform = model.SourePlatform,
                                                        msg = model.msg
                                                    });
                                                    buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(returnMessage));
                                                    await desSocket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                                }
                                                else
                                                {
                                                    //释放websoket对象
                                                    SocketInfo ws = null;
                                                    socket_list.TryRemove(model.touid, out ws);
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Logger.WriteLog("服务端转发消息异常:" + ex.Message);
                                            }
                                        }

                                        //心跳连接
                                        returnMessage = "{\"type\":\"open_health\",\"uid\":\"" + model.uid + "\",\"message\":\"连接成功!\"}";
                                        buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(returnMessage));
                                        await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                    }
                                }
                                else if (model.type == "open_health")
                                {
                                    //服务端发送心跳回复信息
                                    returnMessage = "{\"type\":\"open_health_ack\",\"uid\":\"" + model.uid + "\",\"message\":\"服务端发送心跳!\"}";
                                    buffer = new ArraySegment<byte>(Encoding.UTF8.GetBytes(returnMessage));
                                    await socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
                                }
                                else if (model.type == "open_close")
                                {
                                    //关闭连接
                                    SocketInfo ws = null;
                                    socket_list.TryRemove(model.uid, out ws);
                                    await socket.CloseOutputAsync(WebSocketCloseStatus.Empty, string.Empty, CancellationToken.None);//如果client发起close请求，对client进行ack
                                }
                                else
                                {
                                    //其他,未知消息
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.WriteLog("异常:" + ex.Message);
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                catch
                {
                    //释放websoket对象
                    SocketInfo ws = null;
                    bool isSuccess = socket_list.TryGetValue(userid, out ws);
                    if (isSuccess && socket.Equals(ws))
                    {
                        socket_list.TryRemove(userid, out ws);
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 查询用户在线状态
        /// </summary>
        /// <param name="AccountId"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("GetOnlineState")]
        public IHttpActionResult GetOnlineState(string AccountId)
        {
            return Json<dynamic>(new
            {
                code = 0,
                data = new
                {
                    IsOnline = socket_list.ContainsKey(AccountId) && socket_list[AccountId].socket.State == WebSocketState.Open
                }
            });
        }

        /// <summary>
        /// 获取所有连接状态
        /// </summary>
        /// <param name="AccountId"></param>
        /// <returns></returns>
        [HttpGet]
        [Route("GetAllConnections")]
        public IHttpActionResult GetAllConnections()
        {
            return Json<dynamic>(new
            {
                code = 0,
                total = socket_list.Count(),
                data = socket_list
            });
        }

    }

}
