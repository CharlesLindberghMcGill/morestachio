﻿#region

using System;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using Morestachio.Attributes;
using Morestachio.Formatter;
using Morestachio.Framework;

#endregion

namespace Morestachio
{
	/// <summary>
	///		Delegate for the Event Handler in <see cref="ParserOptions.UnresolvedPath"/>
	/// </summary>
	/// <param name="path"></param>
	/// <param name="type"></param>
	public delegate void InvalidPath(string path, [CanBeNull]Type type);

	/// <summary>
	///     Options for Parsing run
	/// </summary>
	[PublicAPI]
	public class ParserOptions
	{
		[NotNull]
		private IFormatterMatcher _formatters;

		/// <summary>
		///     ctor
		/// </summary>
		/// <param name="template"></param>
		public ParserOptions([NotNull]string template)
			: this(template, null)
		{
		}

		/// <summary>
		///     ctor
		/// </summary>
		/// <param name="template"></param>
		/// <param name="sourceStream">The factory that is used for each template generation</param>
		public ParserOptions([NotNull]string template, Func<Stream> sourceStream)
			: this(template, sourceStream, null)
		{
		}

		/// <summary>
		///     Initializes a new instance of the <see cref="ParserOptions" /> class.
		/// </summary>
		/// <param name="template">The template.</param>
		/// <param name="sourceStream">The source stream.</param>
		/// <param name="encoding">The encoding.</param>
		public ParserOptions([CanBeNull]string template, Func<Stream> sourceStream, Encoding encoding)
		{
			Template = template ?? "";
			SourceFactory = sourceStream ?? (() => new MemoryStream());
			Encoding = encoding ?? Encoding.UTF8;
			_formatters = new FormatterMatcher();
			Null = string.Empty;
			MaxSize = 0;
			DisableContentEscaping = false;
			WithModelInference = false;
			Timeout = TimeSpan.Zero;
			PartialStackSize = 255;
		}

		/// <summary>
		///     Initializes a new instance of the <see cref="ParserOptions" /> class.
		/// </summary>
		/// <param name="template">The template.</param>
		/// <param name="sourceStream">The source stream.</param>
		/// <param name="encoding">The encoding.</param>
		/// <param name="maxSize">The maximum size.</param>
		/// <param name="disableContentEscaping">if set to <c>true</c> [disable content escaping].</param>
		/// <param name="withModelInference">if set to <c>true</c> [with model inference].</param>
		public ParserOptions([NotNull]string template, Func<Stream> sourceStream, Encoding encoding, long maxSize,
			bool disableContentEscaping = false, bool withModelInference = false)
			: this(template, sourceStream, encoding)
		{
			MaxSize = maxSize;
			DisableContentEscaping = disableContentEscaping;
			WithModelInference = withModelInference;
		}

		/// <summary>
		///     Initializes a new instance of the <see cref="ParserOptions" /> class.
		/// </summary>
		/// <param name="template">The template.</param>
		/// <param name="sourceStream">The source stream.</param>
		/// <param name="encoding">The encoding.</param>
		/// <param name="disableContentEscaping">if set to <c>true</c> [disable content escaping].</param>
		/// <param name="withModelInference">if set to <c>true</c> [with model inference].</param>
		public ParserOptions([NotNull]string template, Func<Stream> sourceStream, Encoding encoding,
			bool disableContentEscaping = false, bool withModelInference = false)
			: this(template, sourceStream, encoding, 0, disableContentEscaping, withModelInference)
		{
		}

		/// <summary>
		///		If set to True morestachio will profile the execution and report the result in both <seealso cref="MorestachioDocumentInfo"/> and <seealso cref=""/>
		/// </summary>
		public bool ProfileExecution { get; set; }

		/// <summary>
		///		Can be used to resolve values from custom objects
		/// </summary>
		public IValueResolver ValueResolver { get; set; }

		/// <summary>
		///		Can be used to observe unresolved paths
		/// </summary>
		public event InvalidPath UnresolvedPath;

		///// <summary>
		/////		See <see cref="IPartialTemplateProvider"/>
		///// </summary>
		//public IPartialTemplateProvider PartialTemplateProvider { get; set; }

		/// <summary>
		///     Adds an Formatter overwrite or new Formatter for an Type
		/// </summary>
		[NotNull]
		public IFormatterMatcher Formatters
		{
			get { return _formatters; }
			set
			{
				_formatters = value ?? throw new InvalidOperationException("You must set the Formatters matcher");
			}
		}

		/// <summary>
		///		Gets or sets the max Stack size for nested Partials in execution. Recommended to be not exceeding 2000. Defaults to 255.
		/// </summary>
		public uint PartialStackSize { get; set; }

		/// <summary>
		///		Defines how the Parser should behave when encountering a the PartialStackSize to be exceeded.
		///		Default is <see cref="PartialStackOverflowBehavior.FailWithException"/>
		/// </summary>
		public PartialStackOverflowBehavior StackOverflowBehavior { get; set; }

		/// <summary>
		///		Defines how the Parser should behave when encountering a the PartialStackSize to be exceeded
		/// </summary>
		public enum PartialStackOverflowBehavior
		{
			/// <summary>
			///		Throw a <see cref="MustachioStackOverflowException"/>
			/// </summary>
			FailWithException,
			/// <summary>
			///		Do nothing and skip further calls
			/// </summary>
			FailSilent
		}

		/// <summary>
		///		Gets or sets the timeout. After the timeout is reached and the Template has not finished Processing and Exception is thrown.
		///		For no timeout use <code>TimeSpan.</code>
		/// </summary>
		/// <value>
		/// The timeout.
		/// </value>
		public TimeSpan Timeout { get; set; }

		/// <summary>
		///     The template content to parse.
		/// </summary>
		[NotNull]
		public string Template { get; }

		/// <summary>
		///     In some cases, content should not be escaped (such as when rendering text bodies and subjects in emails).
		///     By default, we use no content escaping, but this parameter allows it to be enabled.
		/// </summary>
		public bool DisableContentEscaping { get; }

		/// <summary>
		///     Parse the template, and capture paths used in the template to determine a suitable structure for the required
		///     model.
		/// </summary>
		public bool WithModelInference { get; }

		/// <summary>
		///     Defines a Max size for the Generated Template.
		///     Zero for unlimited
		/// </summary>
		public long MaxSize { get; }

		/// <summary>
		///     SourceFactory can be used to create a new stream for each template. Default is
		///     <code>() => new MemoryStream()</code>
		/// </summary>
		[NotNull]
		public Func<Stream> SourceFactory { get; }

		/// <summary>
		///     In what encoding should the text be written
		///     Default is <code>Encoding.Utf8</code>
		/// </summary>
		[NotNull]
		public Encoding Encoding { get; }

		/// <summary>
		///     Defines how NULL values are exposed to the Template default is <code>String.Empty</code>
		/// </summary>
		[NotNull]
		public string Null { get; set; }

		internal ParserOptions WithPartial(string partialTemplateTemplate)
		{
			return new ParserOptions(partialTemplateTemplate, SourceFactory, Encoding, DisableContentEscaping, WithModelInference)
			{
				Null = Null,
				StackOverflowBehavior = StackOverflowBehavior,
				Formatters = Formatters,
				Timeout = Timeout
			};
		}

		internal void OnUnresolvedPath(string path, Type type)
		{
			UnresolvedPath?.Invoke(path, type);
		}
	}
}