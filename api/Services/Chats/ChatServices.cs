using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;
using api.Models;
using api.Models.Entity.NormalDB;
using api.Models.Respone;
using api.Models.Websocket.Chat;
using api.utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

//hi why we have usertype and senderType what i doing
namespace api.Services.Chats
{
    public interface IChatServices
    {
        Task<string> CreateChatRoom(int customerID);
        Task handleConnection(WebSocket socket, string roomKey, int userID, ChatSender userType);
        int GetCurrentRoomCount();
        int GetRoomMember(string roomKey);
        Task<ICollection<ChatMessage>> GetChatHistory(string roomKey, int userID);
        Task<PagedResponse<IEnumerable<ChatResponseDto>>> GetChatRooms(int userID, bool isAdmin, int page, int recordsPerPage);
    }
    public class ChatServices : IChatServices
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan timeout = TimeSpan.FromMinutes(1);
        //first string is a chatRoomId
        private static ConcurrentDictionary<string, IList<ChatUser>> chatRoom = new ConcurrentDictionary<string, IList<ChatUser>>();
        public ChatServices(IServiceScopeFactory serviceScopeFactory, IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _configuration = configuration;
        }

        public async Task<string> CreateChatRoom(int customerID)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                NormalDataBaseContext normalDataBaseContext = scope.ServiceProvider.GetRequiredService<NormalDataBaseContext>();
                string roomKey = GenerateUniqueRoomKey(normalDataBaseContext);
                await CreateNewChat(normalDataBaseContext, roomKey, customerID);
                chatRoom.TryAdd(roomKey, new List<ChatUser>());
                return roomKey;
            }
        }

        private string GenerateUniqueRoomKey(NormalDataBaseContext normalDataBaseContext)
        {
            string roomKey;
            do
            {
                roomKey = Guid.NewGuid().ToString();
            } while (normalDataBaseContext.Chats.Any(x => x.chatRoomID == roomKey));
            return roomKey;
        }

        private async Task CreateNewChat(NormalDataBaseContext normalDataBaseContext, string roomKey, int customerID)
        {
            ChatModel newChat = new ChatModel
            {
                chatRoomID = roomKey,
                customerID = customerID,
                chatRoomStatus = ChatRoomStatus.Active,
                createdAt = DateTime.Now,
                updatedAt = DateTime.Now
            };
            normalDataBaseContext.Chats.Add(newChat);
            await normalDataBaseContext.SaveChangesAsync();
        }


        public async Task handleConnection(WebSocket socket, string roomKey, int userID, ChatSender userType)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                Console.WriteLine(roomKey);
                NormalDataBaseContext normalDataBaseContext = scope.ServiceProvider.GetRequiredService<NormalDataBaseContext>();
                ChatModel chat = normalDataBaseContext.Chats.FirstOrDefault(x => x.chatRoomID == roomKey);
                if (chat == null)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Chat room not found", CancellationToken.None);
                    return;
                }

                if (userType != ChatSender.Staff && chat.customerID != userID)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Unauthorized", CancellationToken.None);
                    return;
                }
                if (chat.chatRoomStatus == ChatRoomStatus.Ended)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Chat room has ended", CancellationToken.None);
                    return;
                }
                else
                {
                    if (!chatRoom.ContainsKey(roomKey))
                    {
                        chatRoom.TryAdd(roomKey, new List<ChatUser>());
                    }
                }
            }

            ChatUser? userToKick = chatRoom[roomKey].FirstOrDefault(x => x.UserId == userID && x.UserType == userType);

            if (userToKick != null)
            {
                //kick function
                await userToKick.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Duplicated connection", CancellationToken.None);
                chatRoom[roomKey].Remove(userToKick);
            }

            ChatUser newUser = new ChatUser
            {
                UserId = userID,
                UserType = userType,
                WebSocket = socket
            };

            chatRoom[roomKey].Add(newUser);
            await SendMessage(roomKey, userType.ToString() + userID + " User has joined the chat", true);
            await ReceiveMessage(socket, roomKey, userID, userType);
        }

        public async Task ReceiveMessage(WebSocket socket, string roomKey, int userID, ChatSender userType)
        {
            WebSocketReceiveResult result;
            byte[] buffer = new byte[1024 * 4];

            while (socket.State == WebSocketState.Open)
            {
                try
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("OperationCanceledException");
                    Console.WriteLine(socket.State);
                    break;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    Console.WriteLine(socket.State);
                    break;
                }

                if (result.EndOfMessage && result.MessageType == WebSocketMessageType.Text)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    // ChatMessage chatMessage = JsonConvert.DeserializeObject<ChatMessage>(message);
                    // await SendMessage(roomKey, chatMessage.message, false, chatMessage.senderID, chatMessage.chatSender);
                    await SendMessage(roomKey, message, false, userID, userType); //this is for testing
                    await SaveMessage(roomKey, userID, userType, message);
                }
                else if (result.MessageType == WebSocketMessageType.Close || socket.State == WebSocketState.Aborted)
                {
                    await UserExit(roomKey, userID, userType, "User has left the chat");
                    break;
                }

            }
        }

        private async Task UserExit(string roomKey, int userID, ChatSender userType, string message)
        {
            ChatUser? userToKick = chatRoom[roomKey].FirstOrDefault(x => x.UserId == userID && x.UserType == userType);
            if (userToKick != null)
            {
                await userToKick.WebSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, message, CancellationToken.None);
                chatRoom[roomKey].Remove(userToKick);
                if (chatRoom[roomKey].Count == 0)
                {
                    await EndChatRoom(roomKey);
                }
            }
        }

        private async Task EndChatRoom(string roomKey)
        {
            await Task.Delay(TimeSpan.FromMinutes(5));
            if (chatRoom.ContainsKey(roomKey) && chatRoom[roomKey].Count == 0)
            {
                chatRoom.TryRemove(roomKey, out _);
                using (var scope = _serviceScopeFactory.CreateScope())
                {
                    NormalDataBaseContext normalDataBaseContext = scope.ServiceProvider.GetRequiredService<NormalDataBaseContext>();
                    ChatModel? chat = normalDataBaseContext.Chats.FirstOrDefault(x => x.chatRoomID == roomKey);
                    if (chat != null)
                    {
                        chat.chatRoomStatus = ChatRoomStatus.Ended;
                        chat.updatedAt = DateTime.Now;
                        await normalDataBaseContext.SaveChangesAsync();
                    }
                }
            }
        }

        private async Task SendMessage(string roomKey, string message, bool isToAll, int? sourceID = null, ChatSender? sourceUserType = null)
        {
            Console.WriteLine("SendMessage" + roomKey + message + isToAll + sourceID + sourceUserType);
            Console.WriteLine(chatRoom[roomKey].Count);
            if (isToAll)
            {
                foreach (ChatUser user in chatRoom[roomKey])
                {
                    await user.WebSocket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
                }
                return;
            }
            ChatUser[] chatUsers = chatRoom[roomKey].Where(chatUser => !(chatUser.UserId == sourceID && chatUser.UserType == sourceUserType)).ToArray();
            Console.WriteLine(JsonConvert.SerializeObject(chatUsers));
            foreach (ChatUser user in chatUsers)
            {
                await user.WebSocket.SendAsync(Encoding.UTF8.GetBytes(message), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private async Task SaveMessage(string roomKey, int userID, ChatSender userType, string message)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                NormalDataBaseContext normalDataBaseContext = scope.ServiceProvider.GetRequiredService<NormalDataBaseContext>();
                ChatModel? chat = normalDataBaseContext.Chats.FirstOrDefault(x => x.chatRoomID == roomKey);
                if (chat == null)
                {
                    return;
                }
                ICollection<ChatMessage> chatHistories;

                if (chat.history == null)
                {
                    chatHistories = new List<ChatMessage>();
                }
                else
                {
                    chatHistories = JsonConvert.DeserializeObject<ICollection<ChatMessage>>(chat.history)!;
                }

                ChatMessage chatHistory = new ChatMessage
                {
                    message = message,
                    senderID = userID,
                    chatSender = userType.ToString(),
                    createdAt = DateTime.Now
                };

                chatHistories.Add(chatHistory);
                chat.updatedAt = DateTime.Now;
                chat.history = JsonConvert.SerializeObject(chatHistories);
                await normalDataBaseContext.SaveChangesAsync();
            }
        }



        public int GetCurrentRoomCount()
        {
            return chatRoom.Count;
        }

        public int GetRoomMember(string roomKey)
        {
            if (chatRoom.ContainsKey(roomKey))
            {
                return chatRoom[roomKey].Count;
            }
            return 0;
        }

        public async Task<ICollection<ChatMessage>> GetChatHistory(string roomKey, int userID)
        {
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                NormalDataBaseContext normalDataBaseContext = scope.ServiceProvider.GetRequiredService<NormalDataBaseContext>();
                ChatModel? chat = normalDataBaseContext.Chats.FirstOrDefault(x => x.chatRoomID == roomKey);
                Console.WriteLine(JsonConvert.SerializeObject(chat));
                if (chat == null)
                {
                    return new List<ChatMessage>();
                }
                //TODO: ADD exception
                Console.WriteLine(chat.customerID + " " + userID + " " + (chat.customerID != userID));
                // if (chat.customerID != userID)
                // {
                //     return new List<ChatMessage>();
                // }
                if (chat.history == null)
                {
                    return new List<ChatMessage>();
                }
                return JsonConvert.DeserializeObject<ICollection<ChatMessage>>(chat.history)!;
            }
        }

        public async Task<PagedResponse<IEnumerable<ChatResponseDto>>> GetChatRooms(int userID, bool isAdmin, int page, int recordsPerPage)
        {
            PagedResponse<IEnumerable<ChatResponseDto>> response = new PagedResponse<IEnumerable<ChatResponseDto>>
            {
                CurrentPage = page,
                PageSize = recordsPerPage,
                TotalCount = 0,
                TotalPages = 0,
                Data = null
            };
            using (var scope = _serviceScopeFactory.CreateScope())
            {
                NormalDataBaseContext normalDataBaseContext = scope.ServiceProvider.GetRequiredService<NormalDataBaseContext>();
                IEnumerable<ChatResponseDto> chatRooms;
                if (isAdmin)
                {
                    int totalRecords = await normalDataBaseContext.Chats.CountAsync();
                    response.TotalCount = totalRecords;
                    response.TotalPages = (int)Math.Ceiling((double)totalRecords / recordsPerPage);

                    chatRooms = await normalDataBaseContext.Chats.OrderByDescending(x => x.chatRoomStatus == ChatRoomStatus.Active).ThenByDescending(x => x.updatedAt)
                        .Include(x => x.customer)
                        .Skip(recordsPerPage * (page - 1))
                        .Take(recordsPerPage)
                        .Select(x => new ChatResponseDto
                        {
                            chatRoomID = x.chatRoomID,
                            customerID = x.customerID,
                            customerName = x.customer.lastName + " " + x.customer.firstName,
                            chatRoomStatus = x.chatRoomStatus.ToString(),
                            createdAt = x.createdAt,
                            endedAt = x.endedAt,
                            updatedAt = x.updatedAt,
                            history = x.history == null ? null : JsonConvert.DeserializeObject<ICollection<ChatMessage>>(x.history)
                        }).ToListAsync();
                }
                else
                {
                    int totalRecords = await normalDataBaseContext.Chats.CountAsync(x => x.customerID == userID);
                    response.TotalCount = totalRecords;
                    response.TotalPages = (int)Math.Ceiling((double)totalRecords / recordsPerPage);

                    chatRooms = await normalDataBaseContext.Chats.Where(x => x.customerID == userID)
                        .OrderByDescending(x => x.chatRoomStatus == ChatRoomStatus.Active)
                        .ThenByDescending(x => x.updatedAt)
                        .Include(x => x.customer)
                        .Skip(recordsPerPage * (page - 1))
                        .Take(recordsPerPage)
                        .Select(x => new ChatResponseDto
                        {
                            chatRoomID = x.chatRoomID,
                            customerID = x.customerID,
                            customerName = x.customer.lastName + " " + x.customer.firstName,
                            chatRoomStatus = x.chatRoomStatus.ToString(),
                            createdAt = x.createdAt,
                            endedAt = x.endedAt,
                            updatedAt = x.updatedAt,
                            history = x.history == null ? null : JsonConvert.DeserializeObject<ICollection<ChatMessage>>(x.history)
                        }).ToListAsync();
                }
                response.Data = chatRooms;
                return response;
            }
        }

    }
}