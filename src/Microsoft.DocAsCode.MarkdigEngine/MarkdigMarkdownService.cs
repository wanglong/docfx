// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.DocAsCode.MarkdigEngine
{
    using System;
    using System.Collections.Immutable;
    using System.IO;
    using System.Linq;

    using MarkdigEngine.Extensions;

    using Markdig;
    using Markdig.Renderers;
    using Markdig.Syntax;
    using Microsoft.DocAsCode.Common;
    using Microsoft.DocAsCode.Plugins;

    public class MarkdigMarkdownService : IMarkdownService
    {
        public string Name => "markdig";

        private readonly MarkdownServiceParameters _parameters;
        private readonly MarkdownValidatorBuilder _mvb;
        private readonly MarkdownContext _context;

        public MarkdigMarkdownService(
            MarkdownServiceParameters parameters,
            ICompositionContainer container = null)
        {
            _parameters = parameters;
            _mvb = new MarkdownValidatorBuilderCreator(parameters, container).CreateMarkdownValidatorBuilder();
            _context = new MarkdownContext(
                _parameters.Tokens,
                ReadFile,
                GetLink);
        }

        public MarkupResult Markup(string content, string filePath)
        {
            return Markup(content, filePath, false);
        }

        public MarkupResult Markup(string content, string filePath, bool enableValidation)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (filePath == null)
            {
                throw new ArgumentException("file path can't be null or empty.");
            }

            var pipeline = CreateMarkdownPipeline(isInline: false, enableValidation: enableValidation);

            using (InclusionContext.PushFile((RelativePath)filePath))
            {
                return new MarkupResult
                {
                    Html = Markdown.ToHtml(content, pipeline),
                    Dependency = InclusionContext.Dependencies.Select(file => (string)(RelativePath)file).ToImmutableArray()
                };
            }
        }

        public MarkdownDocument Parse(string content, string filePath)
        {
            return Parse(content, filePath, false);
        }

        public MarkdownDocument Parse(string content, string filePath, bool isInline)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentException("file path can't be null or empty.");
            }

            var pipeline = CreateMarkdownPipeline(isInline, enableValidation: false);

            using (InclusionContext.PushFile((RelativePath)filePath))
            {
                var document = Markdown.Parse(content, pipeline);
                document.SetData("filePath", filePath);

                return document;
            }
        }

        public MarkupResult Render(MarkdownDocument document)
        {
            return Render(document, false);
        }

        public MarkupResult Render(MarkdownDocument document, bool isInline)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            var filePath = document.GetData("filePath") as string;
            if (filePath == null)
            {
                throw new ArgumentNullException("file path can't be found in AST.");
            }

            var pipeline = CreateMarkdownPipeline(isInline, enableValidation: false);

            using (InclusionContext.PushFile((RelativePath)filePath))
            using (var writer = new StringWriter())
            {
                var renderer = new HtmlRenderer(writer);
                pipeline.Setup(renderer);
                renderer.Render(document);
                writer.Flush();

                return new MarkupResult
                {
                    Html = writer.ToString(),
                    Dependency = InclusionContext.Dependencies.Select(file => (string)(RelativePath)file).ToImmutableArray()
                };
            }
        }

        private MarkdownPipeline CreateMarkdownPipeline(bool isInline, bool enableValidation)
        {
            object enableSourceInfoObj = null;
            _parameters?.Extensions?.TryGetValue("EnableSourceInfo", out enableSourceInfoObj);

            var enabled = enableSourceInfoObj as bool?;
            var enableSourceInfo = enabled == null || enabled.Value;

            var builder = new MarkdownPipelineBuilder();

            builder.UseDocfxExtensions(_context);
            builder.Extensions.Insert(0, new YamlHeaderExtension());

            if (enableSourceInfo)
            {
                builder.UseLineNumber(file => ((RelativePath)file).RemoveWorkingFolder());
            }

            if (enableValidation)
            {
                builder.UseValidation(_mvb);
            }

            if (isInline)
            {
                builder.UseInlineOnly();
            }

            return builder.Build();
        }

        private static string GetLink(string path, object relativeTo)
        {
            if (RelativePath.IsRelativePath(path) && PathUtility.IsRelativePath(path) && !RelativePath.IsPathFromWorkingFolder(path) && !path.StartsWith("#"))
            {
                return ((RelativePath)relativeTo + (RelativePath)path).RemoveWorkingFolder();
            }
            return path;
        }

        private (string content, object file) ReadFile(string path, object relativeTo)
        {
            if (!PathUtility.IsRelativePath(path))
            {
                return (null, null);
            }

            var currentFilePath = ((RelativePath)relativeTo).GetPathFromWorkingFolder();
            var includedFilePath = ((RelativePath)path).BasedOn(currentFilePath);
            var includedFilePathWithoutWorkingFolder = includedFilePath.RemoveWorkingFolder();

            if (!EnvironmentContext.FileAbstractLayer.Exists(includedFilePathWithoutWorkingFolder))
            {
                return (null, null);
            }

            var content = EnvironmentContext.FileAbstractLayer.ReadAllText(includedFilePathWithoutWorkingFolder);

            return (content, includedFilePath);
        }
    }
}
