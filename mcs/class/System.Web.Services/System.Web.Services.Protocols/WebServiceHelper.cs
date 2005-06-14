//
// System.Web.Services.Protocols.WebServiceHelper.cs
//
// Author:
//   Lluis Sanchez Gual (lluis@ximian.com)
//
// Copyright (C) Ximian, Inc. 2003
//

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.IO;
using System.Net;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using System.Web.Services.Description;

namespace System.Web.Services.Protocols
{
	internal class WebServiceHelper
	{
		public const string SoapEnvelopeNamespace = "http://schemas.xmlsoap.org/soap/envelope/";
		static readonly char [] trimChars = { '"', '\'' };
		static readonly bool prettyXml;
		
		static WebServiceHelper ()
		{
			string pxml = Environment.GetEnvironmentVariable ("MONO_WEBSERVICES_PRETTYXML");
			prettyXml = (pxml != null && pxml != "no");
		}
		
		public static XmlTextWriter CreateXmlWriter (Stream s)
		{
			// What a waste of UTF8encoders, but it has to be thread safe.
			XmlTextWriter xtw = new XmlTextWriter (s, new UTF8Encoding (false));
				
			if (prettyXml)
				xtw.Formatting = Formatting.Indented;
				
			return xtw;
		}
		
		public static Encoding GetContentEncoding (string cts, out string content_type)
		{
			string encoding;

			if (cts == null) cts = "";
			
			encoding = "utf-8";
			int start = 0;
			int idx = cts.IndexOf (';');
			if (idx == -1)
				content_type = cts;
			else
				content_type = cts.Substring (0, idx);

			content_type = content_type.Trim ();
			for (start = idx + 1; idx != -1;)
			{
				idx = cts.IndexOf (";", start);
				string body;
				if (idx == -1)
					body = cts.Substring (start);
				else 
				{
					body = cts.Substring (start, idx - start);
					start = idx + 1;
				}
				body = body.Trim ();
				if (body.StartsWith ("charset="))
				{
					encoding = body.Substring (8);
					encoding = encoding.TrimStart (trimChars).TrimEnd (trimChars);
				}
			}

			return Encoding.GetEncoding (encoding);
		}

		public static void WriteSoapMessage (XmlTextWriter xtw, SoapMethodStubInfo method, SoapHeaderDirection dir, object bodyContent, SoapHeaderCollection headers)
		{
			SoapBindingUse methodUse = dir == SoapHeaderDirection.Fault ? SoapBindingUse.Literal : method.Use;
			XmlSerializer bodySerializer = method.GetBodySerializer (dir);
			XmlSerializer headerSerializer = method.GetHeaderSerializer (dir);
			object[] headerArray = method.GetHeaderValueArray (dir, headers);
			WriteSoapMessage (xtw, methodUse, bodySerializer, headerSerializer, bodyContent, headerArray);
		}
		
		public static void WriteSoapMessage (XmlTextWriter xtw, SoapBindingUse methodUse, XmlSerializer bodySerializer, XmlSerializer headerSerializer, object bodyContent, object[] headers)
		{
			xtw.WriteStartDocument ();
			xtw.WriteStartElement ("soap", "Envelope", WebServiceHelper.SoapEnvelopeNamespace);
			xtw.WriteAttributeString ("xmlns", "xsi", null, XmlSchema.InstanceNamespace);
			xtw.WriteAttributeString ("xmlns", "xsd", null, XmlSchema.Namespace);

			// Serialize headers
			if (headers != null)
			{
				xtw.WriteStartElement ("soap", "Header", WebServiceHelper.SoapEnvelopeNamespace);
				headerSerializer.Serialize (xtw, headers);
				xtw.WriteEndElement ();
			}

			// Serialize body
			xtw.WriteStartElement ("soap", "Body", WebServiceHelper.SoapEnvelopeNamespace);
			
			if (methodUse == SoapBindingUse.Encoded)
				xtw.WriteAttributeString ("encodingStyle", WebServiceHelper.SoapEnvelopeNamespace, "http://schemas.xmlsoap.org/soap/encoding/");
				
			bodySerializer.Serialize (xtw, bodyContent);

			xtw.WriteEndElement ();
			xtw.WriteEndElement ();
			xtw.Flush ();
		}

		public static void ReadSoapMessage (XmlTextReader xmlReader, SoapMethodStubInfo method, SoapHeaderDirection dir, out object body, out SoapHeaderCollection headers)
		{
			XmlSerializer bodySerializer = method.GetBodySerializer (dir);
			XmlSerializer headerSerializer = method.GetHeaderSerializer (dir);
			ReadSoapMessage (xmlReader, bodySerializer, headerSerializer, out body, out headers);
		}
		
		public static void ReadSoapMessage (XmlTextReader xmlReader, XmlSerializer bodySerializer, XmlSerializer headerSerializer, out object body, out SoapHeaderCollection headers)
		{
			xmlReader.MoveToContent ();
			xmlReader.ReadStartElement ("Envelope", WebServiceHelper.SoapEnvelopeNamespace);

			headers = ReadHeaders (xmlReader, headerSerializer);

			xmlReader.MoveToContent ();
			xmlReader.ReadStartElement ("Body", WebServiceHelper.SoapEnvelopeNamespace);
			xmlReader.MoveToContent ();
			
			if (xmlReader.LocalName == "Fault" && xmlReader.NamespaceURI == SoapEnvelopeNamespace)
				bodySerializer = Fault.Serializer;

			body = bodySerializer.Deserialize (xmlReader);
		}

		static SoapHeaderCollection ReadHeaders (XmlTextReader xmlReader, XmlSerializer headerSerializer)
		{
			SoapHeaderCollection headers = null;
			while (! (xmlReader.NodeType == XmlNodeType.Element && xmlReader.LocalName == "Body" && xmlReader.NamespaceURI == WebServiceHelper.SoapEnvelopeNamespace))
			{
				if (xmlReader.NodeType == XmlNodeType.Element && xmlReader.LocalName == "Header" 
				    && xmlReader.NamespaceURI == WebServiceHelper.SoapEnvelopeNamespace && !xmlReader.IsEmptyElement
				    && headerSerializer != null)
				{
					xmlReader.ReadStartElement ();
					xmlReader.MoveToContent ();
					
					HeaderSerializationHelper uh = new HeaderSerializationHelper (headerSerializer);
					headers = uh.Deserialize (xmlReader);
					
					while (xmlReader.NodeType != XmlNodeType.EndElement)
						xmlReader.Skip ();
						
					xmlReader.ReadEndElement ();
				}
				else
					xmlReader.Skip ();
			}
			if (headers != null)
				return headers;
			else
				return new SoapHeaderCollection ();
		}
		
		class HeaderSerializationHelper
		{
			SoapHeaderCollection headers;
			XmlSerializer headerSerializer;
			
			public HeaderSerializationHelper (XmlSerializer headerSerializer)
			{
				this.headers = new SoapHeaderCollection ();
				this.headerSerializer = headerSerializer;
			}
			
			public SoapHeaderCollection Deserialize (XmlTextReader xmlReader)
			{
				try {
					headerSerializer.UnknownElement += new XmlElementEventHandler (OnAddUnknownHeader);
					object[] headerArray = (object[]) headerSerializer.Deserialize (xmlReader);
					foreach (SoapHeader h in headerArray)
						if (h != null) headers.Add (h);
					return headers;
				} finally {
					headerSerializer.UnknownElement -= new XmlElementEventHandler (OnAddUnknownHeader);
				}
			}
			
			void OnAddUnknownHeader (object sender, XmlElementEventArgs e)
			{
				headers.Add (new SoapUnknownHeader (e.Element));
			}
		}

		public static void InvalidOperation (string message, WebResponse response, Encoding enc)
		{
			if (response == null)
				throw new InvalidOperationException (message);

			if (enc == null)
				enc = Encoding.UTF8;

			StringBuilder sb = new StringBuilder ();
			sb.Append (message);
			if (response.ContentLength > 0) {
				sb.Append ("\r\nResponse error message:\r\n--\r\n");

				try {
					StreamReader resp = new StreamReader (response.GetResponseStream (), enc);
					sb.Append (resp.ReadToEnd ());
				} catch (Exception) {
				}
			}

			throw new InvalidOperationException (sb.ToString ());
		}
	}
}
