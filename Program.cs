using System;
using System.Net;
using System.Text;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace kaplat_server
{
    class Program
    {
        private static HttpListener server;
        private static endPoint eEndPoint;
        private static HttpListenerContext context;
        private static Dictionary<string, Task> taskMap = new Dictionary<string, Task>();
        private static JObject result = new JObject();

        private static void Main()
        {
            startServer();
            while (true)
            {
                context = server.GetContext();
                context.Response.ContentType = "application/json";
                eEndPoint = getEndPoint(context);
                switch (eEndPoint)
                {
                    case endPoint.Health:
                        checkServerHealth();
                        break;
                    case endPoint.Create:
                        createTODO();
                        break;
                    case endPoint.Size:
                        getTODOsCount();
                        break;
                    case endPoint.Content:
                        getTODOsData();
                        break;
                    case endPoint.Update:
                        updateTODOStatus();
                        break;
                    case endPoint.Delete:
                        deleteTODO();
                        break;
                    default:
                        context.Response.StatusCode = 404;
                        break;
                }
            }
        }

        private static void deleteTODO()
        {
            string idStr = context.Request.QueryString["id"];
            if (!idIsExist(idStr))
            {
                context.Response.StatusCode = 404;
                string errorMsg = $"Error: no such TODO with id {idStr}";
                createJsonResponse("errorMessage", errorMsg);
            }
            else
            {
                context.Response.StatusCode = 200;
                int id = int.Parse(idStr);
                string title = "";
                foreach (KeyValuePair<string, Task> kvp in taskMap)
                {
                    if (kvp.Value.id == id)
                    {
                        title = kvp.Key;
                        break;
                    }
                }
                taskMap.Remove(title);
                createJsonResponse("result", taskMap.Count.ToString());
            }
        }

        private static void updateTODOStatus()
        {
            kaplat_server.Task.Status newStatus;
            string statusParam = context.Request.QueryString["status"];
            string idStr = context.Request.QueryString["id"];
            if (!idIsExist(idStr))
            {
                context.Response.StatusCode = 404;
                string errorMsg = $"Error: no such TODO with id {idStr}";
                createJsonResponse("errorMessage", errorMsg);
            }
            else if (!Enum.TryParse(statusParam, true, out newStatus))
            {
                context.Response.StatusCode = 400;
            }
            else
            {
                string oldStatus = "";
                int id = int.Parse(idStr);
                foreach (KeyValuePair<string, Task> kvp in taskMap)
                {
                    if (kvp.Value.id == id)
                    {
                        oldStatus = kvp.Value.eStatus.ToString();
                        kvp.Value.eStatus = newStatus;
                        break;
                    }
                }
                context.Response.StatusCode = 200;
                createJsonResponse("result", oldStatus);
            }
        }

        private static bool idIsExist(string idStr)
        {
            bool exist = false;
            int id = int.Parse(idStr);
            foreach (KeyValuePair<string, Task> kvp in taskMap)
            {
                if (kvp.Value.id == id)
                {
                    exist = true;
                }
            }
            return exist;
        }

        private static void getTODOsData()
        {
            Status status;
            sortBy eSortBy;
            string statusParam = context.Request.QueryString["status"];
            string sortByParam = context.Request.QueryString["sortBy"];
            if (statusOutOfRange(statusParam) || (sortByParam != null && sortByOutOfRange(sortByParam)))
            {
                context.Response.StatusCode = 400;
            }
            else
            {
                Enum.TryParse(statusParam, true, out status);
                Enum.TryParse(sortByParam, true, out eSortBy);
                context.Response.StatusCode = 200;
                List<Task> TODOsContent = filterAndSortTODOs(status, eSortBy);
                JArray arr = new JArray(new JToken[TODOsContent.Count]);
                for (int i = 0; i < TODOsContent.Count; i++)
                {
                    Task task = TODOsContent[i];
                    JObject obj = new JObject();
                    obj.Add("id", task.id);
                    obj.Add("title", task.title);
                    obj.Add("content", task.content);
                    obj.Add("status", task.eStatus.ToString());
                    obj.Add("dueDate", task.dueDate);
                    arr[i] = obj;
                }
                string jsonResponse = JsonConvert.SerializeObject(arr);
                byte[] buffer = Encoding.UTF8.GetBytes(jsonResponse);
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = buffer.Length;
                context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                context.Response.OutputStream.Close();
            }
        }

        private static List<Task> filterAndSortTODOs(Status status, sortBy eSortBy)
        {
            List<Task> res = new List<Task>();
            foreach (KeyValuePair<string, Task> kvp in taskMap)
            {
                if (kvp.Value.eStatus.ToString() == status.ToString() || status.ToString() == "ALL")
                {
                    res.Add(kvp.Value);
                }
            }
            res.Sort(new TaskComparer(eSortBy));
            return res;
        }

        private static bool sortByOutOfRange(string sortBy)
        {
            bool outOfRange = false;
            if (sortBy != "ID" && sortBy != "DUE_DATE" && sortBy != "TITLE")
            {
                outOfRange = true;
            }
            return outOfRange;
        }

        private static bool statusOutOfRange(string status)
        {
            bool outOfRange = false;
            if (status != "ALL" && status != "PENDING" && status != "LATE" && status != "DONE")
            {
                outOfRange = true;
            }
            return outOfRange;
        }

        private static void getTODOsCount()
        {
            Status status;
            string statusParam = context.Request.QueryString["status"];
            if (statusOutOfRange(statusParam))
            {
                context.Response.StatusCode = 400;
            }
            else
            {
                context.Response.StatusCode = 200;
                Enum.TryParse(statusParam, true, out status);
                switch (status)
                {
                    case Status.ALL:
                        createJsonResponse("result", taskMap.Count.ToString());
                        break;
                    case Status.PENDING:
                        createJsonResponse("result", countStatus(Status.PENDING.ToString()).ToString());
                        break;
                    case Status.LATE:
                        createJsonResponse("result", countStatus(Status.LATE.ToString()).ToString());
                        break;
                    case Status.DONE:
                        createJsonResponse("result", countStatus(Status.DONE.ToString()).ToString());
                        break;
                }
            }
        }

        private static int countStatus(string toCheck)
        {
            int counter = 0;
            foreach (KeyValuePair<string, Task> kvp in taskMap)
            {
                if (kvp.Value.eStatus.ToString() == toCheck)
                {
                    counter++;
                }
            }
            return counter;
        }

        private static void createTODO()
        {
            string requestBody;
            using (StreamReader reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
            {
                requestBody = reader.ReadToEnd();
            }
            JObject requestBodyObject = JObject.Parse(requestBody);
            string title = requestBodyObject["title"].ToString();
            string content = requestBodyObject["content"].ToString();
            long dueDate = (long)requestBodyObject["dueDate"];
            if (goodInput(title, dueDate))
            {
                Task newTask = new Task(title, content, dueDate);
                taskMap.Add(newTask.title, newTask);
                context.Response.StatusCode = 200;
                createJsonResponse("result", newTask.id.ToString());
            }
        }

        private static bool goodInput(string title, long dueDate)
        {
            bool goodInput = true;
            long currentTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if ((taskMap.ContainsKey(title)) || (dueDate <= currentTimestamp))
            {
                goodInput = false;
                string errorStr = "";
                if (taskMap.ContainsKey(title))
                {
                    errorStr = $"Error: TODO with the title {title} already exists in the system";
                }
                else
                {
                    errorStr = "Error: Can’t create new TODO that its due date is in the past";
                }
                createJsonResponse("errorMessage", errorStr);
                context.Response.StatusCode = 409;
            }
            return goodInput;
        }

        private static void createJsonResponse(string key, string value)
        {
            result.RemoveAll();
            result.Add(key, value);
            byte[] buffer = Encoding.UTF8.GetBytes(result.ToString());
            context.Response.ContentLength64 = buffer.Length;
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
        }

        private static void checkServerHealth()
        {
            context.Response.StatusCode = 200;
            byte[] buffer = Encoding.UTF8.GetBytes("OK");
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.Close();
        }

        private static void startServer()
        {
            Stopwatch sw = new Stopwatch();

            sw.Start();
            server = new HttpListener();
            server.Prefixes.Add("http://localhost:8496/");
            server.Start();
            sw.Stop();
            Console.WriteLine("Server listening on port 8496...");
            Console.WriteLine($"It took {sw.ElapsedMilliseconds} milli sec's to start the server");
        }

        private static endPoint getEndPoint(HttpListenerContext context)
        {
            endPoint res = endPoint.NULL;
            if (context.Request.Url.AbsolutePath == "/todo/health")
            {
                res = endPoint.Health;
            }
            else if (context.Request.Url.AbsolutePath == "/todo")
            {
                if (context.Request.HttpMethod == "POST")
                {
                    res = endPoint.Create;
                }
                else if (context.Request.HttpMethod == "PUT")
                {
                    res = endPoint.Update;
                }
                else if (context.Request.HttpMethod == "DELETE")
                {
                    res = endPoint.Delete;
                }
            }
            else if (context.Request.Url.AbsolutePath == "/todo/size")
            {
                res = endPoint.Size;
            }
            else if (context.Request.Url.AbsolutePath == "/todo/content")
            {
                res = endPoint.Content;
            }
            return res;
        }

        enum endPoint
        {
            Health,
            Create,
            Size,
            Content,
            Update,
            Delete,
            NULL
        }
        enum Status
        {
            PENDING,
            LATE,
            DONE,
            ALL
        }
        public enum sortBy
        {
            ID,
            DUE_DATE,
            TITLE
        }
    }

    class Task
    {
        public string title, content;
        public long dueDate;
        public Status eStatus;
        public int id;

        private static int counter = 0;

        public Task(string title, string content, long dueDate)
        {
            this.title = title;
            this.content = content;
            this.dueDate = dueDate;
            id = ++counter;
            eStatus = Status.PENDING;
        }

        public enum Status
        {
            PENDING,
            LATE,
            DONE
        }
    }

    class TaskComparer : IComparer<Task>
    {
        private kaplat_server.Program.sortBy eSortBy;
        public TaskComparer(kaplat_server.Program.sortBy sortBy)
        {
            eSortBy = sortBy;
        }

        public int Compare(Task x, Task y)
        {
            switch (eSortBy)
            {
                case kaplat_server.Program.sortBy.ID:
                    return x.id.CompareTo(y.id);
                case kaplat_server.Program.sortBy.DUE_DATE:
                    return x.dueDate.CompareTo(y.dueDate);
                case kaplat_server.Program.sortBy.TITLE:
                    return string.Compare(x.title, y.title);
                default:
                    throw new ArgumentException("Invalid sort by value");
            }
        }
    }
}