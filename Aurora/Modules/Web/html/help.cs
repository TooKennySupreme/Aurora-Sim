﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Aurora.Framework.Servers.HttpServer;

namespace Aurora.Modules.Web
{
    public class HelpdMain : IWebInterfacePage
    {
        public string[] FilePath
        {
            get
            {
                return new[]
                       {
                           "html/help.html"
                       };
            }
        }

        public bool RequiresAuthentication { get { return false; } }
        public bool RequiresAdminAuthentication { get { return false; } }

        public Dictionary<string, object> Fill(WebInterface webInterface, string filename, OSHttpRequest httpRequest,
            OSHttpResponse httpResponse, Dictionary<string, object> requestParameters, ITranslator translator)
        {
			var vars = new Dictionary<string, object>();
			vars.Add("HelpText", translator.GetTranslatedString("HelpText"));
			vars.Add("HelpViewersConfigText", translator.GetTranslatedString("HelpViewersConfigText"));
			vars.Add("AngstormViewer", translator.GetTranslatedString("AngstormViewer"));
			vars.Add("VoodooViewer", translator.GetTranslatedString("VoodooViewer"));
			vars.Add("AstraViewer", translator.GetTranslatedString("AstraViewer"));
			vars.Add("ImprudenceViewer", translator.GetTranslatedString("ImprudenceViewer"));
			vars.Add("PhoenixViewer", translator.GetTranslatedString("PhoenixViewer"));
			vars.Add("SingularityViewer", translator.GetTranslatedString("SingularityViewer"));
			vars.Add("ZenViewer", translator.GetTranslatedString("ZenViewer"));

			return vars;			
        }

        public bool AttemptFindPage(string filename, ref OSHttpResponse httpResponse, out string text)
        {
            text = "";
            return false;
        }
    }
}
