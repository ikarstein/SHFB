﻿//===============================================================================================================
// System  : Sandcastle Help File Builder MSBuild Tasks
// File    : GenerateMarkdownContent.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 04/05/2017
// Note    : Copyright 2015-2017, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the MSBuild task used to finish up creation of the markdown content and copy it to the
// output folder.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 03/30/2015  EFW  Created the code
//===============================================================================================================

// Ignore Spelling: blockquote dl ol ul noscript fieldset iframe

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace SandcastleBuilder.Utils.MSBuild
{
    /// <summary>
    /// This task is used to finish up creation of the markdown content and copy it to the output folder
    /// </summary>
    public class GenerateMarkdownContent : Task
    {
        #region Private data members
        //=====================================================================

        private static Regex reAddNewLines = new Regex(@"(\w)(\<(p|div|h[1-6]|blockquote|pre|table|dl|ol|ul|" +
            "address|script|noscript|form|fieldset|iframe|math))");
        private static Regex reTrimNbsp = new Regex(@"\s*&nbsp;\s*");

        private static Regex reTrimSpace = new Regex(@"\s+</");

        #endregion

        #region Task input properties
        //=====================================================================

        /// <summary>
        /// This is used to pass in the working folder where the files to parse are located
        /// </summary>
        [Required]
        public string WorkingFolder { get; set; }

        /// <summary>
        /// This is used to pass in the output folder where the generated content is stored
        /// </summary>
        [Required]
        public string OutputFolder { get; set; }

        /// <summary>
        /// This is used to pass in the default topic name.  If no Home.md file is found and a value is
        /// specified, this file will be copied to create Home.md.
        /// </summary>
        public string DefaultTopic { get; set; }

        /// <summary>
        /// This is used to pass in whether or not to append extensions to the sidebar topic links
        /// </summary>
        public bool AppendMarkdownFileExtensionsToUrls { get; set; }

        #endregion

        #region Execute methods
        //=====================================================================

        /// <summary>
        /// This is used to execute the task and perform the build
        /// </summary>
        /// <returns>True on success or false on failure.</returns>
        public override bool Execute()
        {
            XDocument topic;
            string key, title;
            int topicCount = 0;

            // Load the TOC file and process the topics in TOC order.  This generates the sidebar TOC as well.
            using(var tocReader = XmlReader.Create(Path.Combine(this.WorkingFolder, @"..\..\toc.xml")))
                using(StreamWriter sidebar = new StreamWriter(Path.Combine(this.WorkingFolder, "_Sidebar.md")))
                {
                    while(tocReader.Read())
                        if(tocReader.NodeType == XmlNodeType.Element && tocReader.Name == "topic")
                        {
                            key = tocReader.GetAttribute("file");

                            if(!String.IsNullOrWhiteSpace(key))
                            {
                                string topicFile = Path.Combine(this.WorkingFolder, key + ".md");

                                // The topics are easier to update as XDocuments as we can use LINQ to XML to
                                // find stuff.  Not all topics may have been generated by the presentation style.
                                // Ignore those that won't load.
                                try
                                {
                                    topic = XDocument.Load(topicFile);
                                }
                                catch(XmlException )
                                {
                                    // If it's an additional topic added by the user, wrap it in a document
                                    // element and try again.  We still need the title for the TOC.
                                    string content = File.ReadAllText(topicFile);

                                    try
                                    {
                                        topic = XDocument.Parse("<document>\r\n" + content + "\r\n</document>");
                                    }
                                    catch
                                    {
                                        topic = null;
                                    }
                                }

                                if(topic != null)
                                {
                                    title = ApplyChanges(key, topic) ?? key;

                                    // Remove the containing document element and save the inner content
                                    string content = topic.ToString(SaveOptions.DisableFormatting);

                                    int pos = content.IndexOf('>');

                                    if(pos != -1)
                                        content = content.Substring(pos + 1).TrimStart();

                                    pos = content.IndexOf("</document>", StringComparison.Ordinal);

                                    if(pos != -1)
                                        content = content.Substring(0, pos);

                                    // A few final fix ups:
                                    
                                    // Insert line breaks between literal text and block level elements where
                                    // needed.
                                    content = reAddNewLines.Replace(content, "$1\r\n\r\n$2");

                                    // Trim trailing spaces before a closing element
                                    content = reTrimSpace.Replace(content, "</");

                                    // Decode HTML entities.  However, keep non-breaking spaces.
                                    content = WebUtility.HtmlDecode(content).Replace("\xA0", "&nbsp;");

                                    // Trim whitespace around non-breaking spaces to get rid of excess blank
                                    // lines except in a cases where we need to keep it so that the content is
                                    // converted from markdown to HTML properly.
                                    content = reTrimNbsp.Replace(content, new MatchEvaluator(m =>
                                    {
                                        if(m.Index + m.Length < content.Length && content[m.Index + m.Length] == '#')
                                            return "\r\n\r\n";

                                        if(m.Value.Length > 6 && m.Value[0] == '&' && Char.IsWhiteSpace(m.Value[6]))
                                            return "&nbsp;\r\n";

                                        if(Char.IsWhiteSpace(m.Value[0]))
                                            return "\r\n&nbsp;";

                                        return m.Value;
                                    }));

                                    File.WriteAllText(topicFile, content);

                                    if(tocReader.Depth > 1)
                                        sidebar.Write(new String(' ', (tocReader.Depth - 1) * 2));

                                    sidebar.WriteLine("- [{0}]({1}{2})", title, key,
                                        this.AppendMarkdownFileExtensionsToUrls ? ".md" : String.Empty);

                                    topicCount++;

                                    if((topicCount % 500) == 0)
                                        Log.LogMessage(MessageImportance.High, "{0} topics generated", topicCount);
                                }
                            }
                        }
                }

            Log.LogMessage(MessageImportance.High, "Finished generating {0} topics", topicCount);

            string homeTopic = Path.Combine(this.WorkingFolder, "Home.md");

            if(!File.Exists(homeTopic) && !String.IsNullOrWhiteSpace(this.DefaultTopic))
            {
                string defaultTopic = Path.Combine(this.WorkingFolder,
                    Path.GetFileNameWithoutExtension(this.DefaultTopic) + ".md");

                if(File.Exists(defaultTopic))
                    File.Copy(defaultTopic, homeTopic);
            }

            // Copy the working folder content to the output folder
            int fileCount = 0;

            Log.LogMessage(MessageImportance.High, "Copying content to output folder...");

            this.RecursiveCopy(this.WorkingFolder, this.OutputFolder, ref fileCount);

            Log.LogMessage(MessageImportance.High, "Finished copying {0} files", fileCount);

            return true;
        }
        #endregion

        #region Helper methods
        //=====================================================================

        /// <summary>
        /// This applies the changes needed to convert the XML to a markdown topic file
        /// </summary>
        /// <param name="key">The topic key</param>
        /// <param name="topic">The topic to which the changes are applied</param>
        /// <returns>The page title if one could be found</returns>
        private string ApplyChanges(string key, XDocument topic)
        {
            string topicTitle = null;
            var root = topic.Root;

            // Remove the filename element from API topics
            var filename = root.Element("file");

            if(filename != null)
                filename.Remove();

            foreach(var span in topic.Descendants("span").Where(s => s.Attribute("class") != null).ToList())
            {
                string spanClass = span.Attribute("class").Value;

                switch(spanClass)
                {
                    case "languageSpecificText":
                        // Replace language-specific text with the neutral text sub-entry.  If not found,
                        // remove it.
                        var genericText = span.Elements("span").FirstOrDefault(
                            s => (string)s.Attribute("class") == "nu");

                        if(genericText != null)
                            span.ReplaceWith(genericText.Value);
                        else
                            span.Remove();
                        break;

                    default:
                        // All other formatting spans are removed by moving the content up to the parent element.
                        // The children of the LST spans are ignored since we've already handled them.
                        if(span.Parent.Name == "span" && (string)span.Parent.Attribute("class") == "languageSpecificText")
                            break;

                        foreach(var child in span.Nodes().ToList())
                        {
                            child.Remove();
                            span.AddBeforeSelf(child);
                        }

                        span.Remove();
                        break;
                }
            }

            var linkTargets = new Dictionary<string, string>();

            // Remove link ID spans and change any links to them to use the page/section title instead.  Note
            // that cross-page anchor references (PageName#Anchor) won't work and I'm not going to attempt to
            // support them since it would be more complicated.  Likewise, links to elements without a title
            // such as list items and table cells won't work either.
            foreach(var span in topic.Descendants("span").Where(s => s.Attribute("id") != null).ToList())
            {
                string id = span.Attribute("id").Value;
                var sectionTitle = span.PreviousNode as XText;

                if(sectionTitle != null)
                {
                    // We may get more than one line so find the last one with a section title which will be
                    // the closest to the span.
                    string title = sectionTitle.Value.Split(new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries).Reverse().FirstOrDefault(
                            t => t.Trim().Length > 2 && t[0] == '#');

                    if(title != null)
                    {
                        int pos = title.IndexOf(' ');
                        
                        if(pos != -1)
                        {
                            title = title.Substring(pos + 1).Trim();

                            // Extract the topic title for the sidebar TOC
                            if(id == "PageHeader")
                                topicTitle = title;

                            // Convert the title ID to the expected format
                            title = title.ToLowerInvariant().Replace(' ', '-').Replace("#", String.Empty);

                            // For intro links, link to the page header title since intro sections have no title
                            // themselves.  The transformations always add a PageHeader link span after the page
                            // title (or should).
                            if(id.StartsWith("@pageHeader_", StringComparison.Ordinal))
                            {
                                if(linkTargets.ContainsKey(id.Substring(12)))
                                    Log.LogWarning(null, "GMC0001", "GMC0001", "SHFB", 0, 0, 0, 0,
                                        "Duplicate in-page link ID found: Topic ID: {0}  Link ID: {1}", key, id);

                                linkTargets[id.Substring(12)] = "PageHeader";
                            }
                            else
                            {
                                if(linkTargets.ContainsKey(id))
                                    Log.LogWarning(null, "GMC0001", "GMC0001", "SHFB", 0, 0, 0, 0,
                                        "Duplicate in-page link ID found: Topic ID: {0}  Link ID: {1}", key, id);

                                linkTargets[id] = "#" + title;
                            }
                        }
                    }
                }

                span.Remove();
            }

            // Update in-page link targets
            foreach(var anchor in topic.Descendants("a").ToList())
            {
                var href = anchor.Attribute("href");

                if(href.Value.Length > 1 && href.Value[0] == '#')
                {
                    string id = href.Value.Substring(1).Trim(), target;

                    if(linkTargets.TryGetValue(id, out target))
                    {
                        if(target == "PageHeader")
                            if(!linkTargets.TryGetValue("PageHeader", out target))
                                target = "#";
                    }
                    else
                        target = "#";

                    href.Value = target;
                }
            }

            // If we couldn't find a topic title, try to get the first section header title.  It's probably a
            // user-added file.
            if(topicTitle == null)
            {
                var textBlock = topic.DescendantNodes().OfType<XText>().FirstOrDefault();

                if(textBlock != null)
                {
                    string title = textBlock.Value.Split(new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(t => t.Trim().Length > 2 && t[0] == '#');

                    if(title != null)
                    {
                        int pos = title.IndexOf(' ');
                        
                        if(pos != -1)
                            topicTitle = title.Substring(pos + 1).Trim();
                    }
                }
            }

            return topicTitle;
        }

        /// <summary>
        /// This copies files from the specified source folder to the specified destination folder.  If any
        /// subfolders are found below the source folder, the subfolders are also copied recursively.
        /// </summary>
        /// <param name="sourcePath">The source path from which to copy</param>
        /// <param name="destPath">The destination path to which to copy</param>
        /// <param name="fileCount">The file count used for logging progress</param>
        private void RecursiveCopy(string sourcePath, string destPath, ref int fileCount )
        {
            if(sourcePath == null)
                throw new ArgumentNullException("sourcePath");

            if(destPath == null)
                throw new ArgumentNullException("destPath");

            if(destPath[destPath.Length - 1] != '\\')
                destPath += @"\";

            foreach(string name in Directory.EnumerateFiles(sourcePath, "*.*"))
            {
                string filename = destPath + Path.GetFileName(name);

                if(!Directory.Exists(destPath))
                    Directory.CreateDirectory(destPath);

                File.Copy(name, filename, true);

                // All attributes are turned off so that we can delete it later
                File.SetAttributes(filename, FileAttributes.Normal);

                fileCount++;

                if((fileCount % 500) == 0)
                    Log.LogMessage(MessageImportance.High, "Copied {0} files", fileCount);
            }

            // Ignore hidden folders as they may be under source control and are not wanted
            foreach(string folder in Directory.EnumerateDirectories(sourcePath))
                if((File.GetAttributes(folder) & FileAttributes.Hidden) != FileAttributes.Hidden)
                    RecursiveCopy(folder, destPath + folder.Substring(sourcePath.Length + 1), ref fileCount);
        }
        #endregion
    }
}
