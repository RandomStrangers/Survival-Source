﻿// Copyright 2014-2017 ClassicalSharp | Licensed under BSD-3
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
#if ANDROID
using Android.Graphics;
#endif

namespace ClassicalSharp.Network {
	
	/// <summary> Specialised producer and consumer queue for downloading data asynchronously. </summary>
	public class AsyncDownloader : IGameComponent {
		
		EventWaitHandle handle = new EventWaitHandle(false, EventResetMode.AutoReset);
		Thread worker;
		readonly object requestLocker = new object();
		List<Request> requests = new List<Request>();
		readonly object downloadedLocker = new object();
		Dictionary<string, DownloadedItem> downloaded = new Dictionary<string, DownloadedItem>();
		string skinServer = null;
		readonly IDrawer2D drawer;
		
		public Request CurrentItem;
		public int CurrentItemProgress = -3;
		public IDrawer2D Drawer;	
		public AsyncDownloader(IDrawer2D drawer) { this.drawer = drawer; }
		
		
		public void Init(Game game) { Init(game.skinServer); }
		public void Ready(Game game) { }
		public void Reset(Game game) {
			lock (requestLocker)
				requests.Clear();
			handle.Set();
		}
		
		public void OnNewMap(Game game) { }
		public void OnNewMapLoaded(Game game) { }
		
		public void Init(string skinServer) {
			this.skinServer = skinServer;
			WebRequest.DefaultWebProxy = null;
			
			worker = new Thread(DownloadThreadWorker, 256 * 1024);
			worker.Name = "ClassicalSharp.AsyncDownloader";
			worker.IsBackground = true;
			worker.Start();
		}
		
		
		/// <summary> Asynchronously downloads a skin. If 'skinName' points to the url then the skin is
		/// downloaded from that url, otherwise it is downloaded from the url 'defaultSkinServer'/'skinName'.png </summary>
		/// <remarks> Identifier is skin_'skinName'.</remarks>
		public void DownloadSkin(string identifier, string skinName) {
			string strippedSkinName = Utils.StripColours(skinName);
			string url = Utils.IsUrlPrefix(skinName, 0) ? skinName :
				skinServer + strippedSkinName + ".png";
			AddRequest(url, true, identifier, RequestType.Bitmap,
			           DateTime.MinValue , null);
		}
		
		/// <summary> Asynchronously downloads a bitmap image from the specified url.  </summary>
		public void DownloadImage(string url, bool priority, string identifier) {
			AddRequest(url, priority, identifier, RequestType.Bitmap,
			           DateTime.MinValue, null);
		}
		
		/// <summary> Asynchronously downloads a string from the specified url.  </summary>
		public void DownloadPage(string url, bool priority, string identifier) {
			AddRequest(url, priority, identifier, RequestType.String,
			           DateTime.MinValue, null);
		}
		
		/// <summary> Asynchronously downloads a byte array. </summary>
		public void DownloadData(string url, bool priority, string identifier) {
			AddRequest(url, priority, identifier, RequestType.ByteArray,
			           DateTime.MinValue, null);
		}
		
		/// <summary> Asynchronously downloads a bitmap image. </summary>
		public void DownloadImage(string url, bool priority, string identifier,
		                          DateTime lastModified, string etag) {
			AddRequest(url, priority, identifier, RequestType.Bitmap,
			           lastModified, etag);
		}
		
		/// <summary> Asynchronously downloads a byte array. </summary>
		public void DownloadData(string url, bool priority, string identifier,
		                         DateTime lastModified, string etag) {
			AddRequest(url, priority, identifier, RequestType.ByteArray,
			           lastModified, etag);
		}
		
		/// <summary> Asynchronously retrieves the content length of the body response. </summary>
		public void RetrieveContentLength(string url, bool priority, string identifier) {
			AddRequest(url, priority, identifier, RequestType.ContentLength,
			           DateTime.MinValue, null);
		}
		
		void AddRequest(string url, bool priority, string identifier,
		                RequestType type, DateTime lastModified, string etag) {
			lock (requestLocker) {
				Request request = new Request(url, identifier, type, lastModified, etag);
				if (priority) {
					requests.Insert(0, request);
				} else {
					requests.Add(request);
				}
			}
			handle.Set();
		}
		
		/// <summary> Informs the asynchronous thread that it should stop processing further requests
		/// and can consequentially exit the for loop.<br/>
		/// Note that this will *block** the calling thread as the method waits until the asynchronous
		/// thread has exited the for loop. </summary>
		public void Dispose() {
			lock (requestLocker)  {
				requests.Insert(0, null);
			}
			
			handle.Set();
			worker.Join();
			((IDisposable)handle).Dispose();
		}
		
		/// <summary> Removes older entries that were downloaded a certain time ago
		/// but were never removed from the downloaded queue. </summary>
		public void PurgeOldEntriesTask(ScheduledTask task) {
			const int seconds = 10;
			lock(downloadedLocker) {
				DateTime now = DateTime.UtcNow;
				List<string> itemsToRemove = new List<string>(downloaded.Count);
				
				foreach (var item in downloaded) {
					DateTime timestamp = item.Value.TimeDownloaded;
					if ((now - timestamp).TotalSeconds > seconds) {
						itemsToRemove.Add(item.Key);
					}
				}
				
				for (int i = 0; i < itemsToRemove.Count; i++) {
					string key = itemsToRemove[i];
					DownloadedItem item;
					downloaded.TryGetValue(key, out item);
					downloaded.Remove(key);
					Bitmap bmp = item.Data as Bitmap;
					if (bmp != null)
						bmp.Dispose();
				}
			}
		}
		
		/// <summary> Returns whether the requested item exists in the downloaded queue.
		/// If it does, it removes the item from the queue and outputs it. </summary>
		/// <remarks> If the asynchronous thread failed to download the item, this method
		/// will return 'true' and 'item' will be set. However, the contents of the 'item' object will be null.</remarks>
		public bool TryGetItem(string identifier, out DownloadedItem item) {
			bool success = false;
			lock(downloadedLocker) {
				success = downloaded.TryGetValue(identifier, out item);
				if (success) {
					downloaded.Remove(identifier);
				}
			}
			return success;
		}

		void DownloadThreadWorker() {
			while (true) {
				Request request = null;
				lock(requestLocker) {
					if (requests.Count > 0) {
						request = requests[0];
						requests.RemoveAt(0);
						if (request == null)
							return;
					}
				}
				if (request != null) {
					CurrentItem = request;
					CurrentItemProgress = -2;
					ProcessRequest(request);
					
					CurrentItem = null;
					CurrentItemProgress = -3;
				} else {
					handle.WaitOne();
				}
			}
		}
		
		void ProcessRequest(Request request) {
			string url = request.Url;
			Utils.LogDebug("Downloading {0} from: {1}", request.Type, url);
			object value = null;
			HttpStatusCode status = HttpStatusCode.OK;
			string etag = null;
			DateTime lastModified = DateTime.MinValue;
			
			try {
				HttpWebRequest req = MakeRequest(request);
				using (HttpWebResponse response = (HttpWebResponse)req.GetResponse()) {
					etag = response.Headers[HttpResponseHeader.ETag];
					if (response.Headers[HttpResponseHeader.LastModified] != null)
						lastModified = response.LastModified;					
					value = DownloadContent(request, response);
				}
			} catch (Exception ex) {
				if (!(ex is WebException || ex is ArgumentException || ex is UriFormatException || ex is IOException)) throw;

				if (ex is WebException) {
					WebException webEx = (WebException)ex;
					if (webEx.Response != null) {
						status = ((HttpWebResponse)webEx.Response).StatusCode;
						webEx.Response.Close();
					}
				}
				
				if (status != HttpStatusCode.OK) {
					Utils.LogDebug("Failed to download (" + (int)status + ") from: " + url);
				} else {
					Utils.LogDebug("Failed to download from: " + url);
				}
			}
			value = CheckIsValidImage(value, url);

			lock(downloadedLocker) {
				DownloadedItem oldItem;
				DownloadedItem newItem = new DownloadedItem(value, request.TimeAdded, url,
				                                            status, etag, lastModified);
				
				if (downloaded.TryGetValue(request.Identifier, out oldItem)) {
					if (oldItem.TimeAdded > newItem.TimeAdded) {
						DownloadedItem old = oldItem;
						oldItem = newItem;
						newItem = old;
					}

					Bitmap oldBmp = oldItem.Data as Bitmap;
					if (oldBmp != null) oldBmp.Dispose();
				}
				downloaded[request.Identifier] = newItem;
			}
		}
		
		object DownloadContent(Request request, HttpWebResponse response) {
			if (request.Type == RequestType.Bitmap) {
				MemoryStream data = DownloadBytes(response);
				return Platform.ReadBmp32Bpp(drawer, data);
			} else if (request.Type == RequestType.String) {
				MemoryStream data = DownloadBytes(response);
				byte[] rawBuffer = data.GetBuffer();
				return Encoding.UTF8.GetString(rawBuffer, 0, (int)data.Length);
			} else if (request.Type == RequestType.ByteArray) {
				MemoryStream data = DownloadBytes(response);
				return data.ToArray();
			} else if (request.Type == RequestType.ContentLength) {
				return response.ContentLength;
			}
			return null;
		}
		
		object CheckIsValidImage(object value, string url) {
			// Mono seems to be returning a bitmap with a native pointer of zero in some weird cases.
			// We can detect this as every single property access raises an ArgumentException.
			try {
				Bitmap bmp = value as Bitmap;
				if (bmp != null) {
					int height = bmp.Height;
				}
				return value;
			} catch (ArgumentException) {
				Utils.LogDebug("Failed to download from: " + url);
				return null;
			}
		}
		
		HttpWebRequest MakeRequest(Request request) {
			HttpWebRequest req = (HttpWebRequest)WebRequest.Create(request.Url);
			req.AutomaticDecompression = DecompressionMethods.GZip;
			req.ReadWriteTimeout = 90 * 1000;
			req.Timeout = 90 * 1000;
			req.Proxy = null;
			req.UserAgent = Program.AppName;
			
			if (request.LastModified != DateTime.MinValue)
				req.IfModifiedSince = request.LastModified;
			if (request.ETag != null)
				req.Headers["If-None-Match"] = request.ETag;
			return req;
		}
		
		static byte[] buffer = new byte[4096 * 8];
		MemoryStream DownloadBytes(HttpWebResponse response) {
			int length = (int)response.ContentLength;
			MemoryStream dst = length > 0 ?
				new MemoryStream(length) : new MemoryStream();
			CurrentItemProgress = length > 0 ? 0 : -1;
			
			using (Stream src = response.GetResponseStream()) {
				int read = 0;
				while ((read = src.Read(buffer, 0, buffer.Length)) > 0) {
					dst.Write(buffer, 0, read);
					if (length <= 0) continue;
					CurrentItemProgress = (int)(100 * (float)dst.Length / length);
				}
			}
			return dst;
		}
	}
	
	public enum RequestType { Bitmap, String, ByteArray, ContentLength }
	
	public sealed class Request {
		
		/// <summary> Full url to GET from. </summary>
		public string Url;
		
		/// <summary> Unique identifier for this request. </summary>
		public string Identifier;
		
		/// <summary> Type of data to return for this request. </summary>
		public RequestType Type;
		
		/// <summary> Point in time this request was added to the fetch queue. </summary>
		public DateTime TimeAdded;
		
		/// <summary> Point in time the item most recently cached. (if at all) </summary>
		public DateTime LastModified;
		
		/// <summary> ETag of the item most recently cached. (if any) </summary>
		public string ETag;
		
		public Request(string url, string identifier, RequestType type,
		               DateTime lastModified, string etag) {
			Url = url;
			Identifier = identifier;
			Type = type;
			TimeAdded = DateTime.UtcNow;
			LastModified = lastModified;
			ETag = etag;
		}
	}
	
	/// <summary> Represents an item that was asynchronously downloaded. </summary>
	public class DownloadedItem {
		
		/// <summary> Contents that were downloaded. </summary>
		public object Data;
		
		/// <summary> Point in time the item was originally added to the download queue. </summary>
		public DateTime TimeAdded;
		
		/// <summary> Point in time the item was fully downloaded. </summary>
		public DateTime TimeDownloaded;
		
		/// <summary> Full URL this item was downloaded from. </summary>
		public string Url;
		
		/// <summary> HTTP status code returned by the server for this request. </summary>
		public HttpStatusCode ResponseCode;
		
		/// <summary> Unique identifier assigned by the server to this item. </summary>
		public string ETag;
		
		/// <summary> Time the server indicates this item was last modified. </summary>
		public DateTime LastModified;
		
		public DownloadedItem(object data, DateTime timeAdded,
		                      string url, HttpStatusCode code,
		                      string etag, DateTime lastModified) {
			Data = data;
			TimeAdded = timeAdded;
			TimeDownloaded = DateTime.UtcNow;
			Url = url;
			ResponseCode = code;
			ETag = etag;
			LastModified = lastModified;
		}
	}
}