﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Morestachio.Formatter;
using Morestachio.Helper;

namespace Morestachio.Framework
{
	/// <summary>
	///     The current context for any given expression
	/// </summary>
	public class ContextObject
	{
		static ContextObject()
		{
			DefaultFormatter = new FormatterMatcher();
			foreach (var type in new[]
			{
				typeof(IFormattable),
			})
			{
				//we have to use a proxy function to get around a changing delegate that maybe overwritten by the user
				//if the user overwrites the static DefaultToStringWithFormatting after we have added it to the list this would
				//have no effect
				DefaultFormatter.AddFormatter(type, new Func<object, object, object>(DefaultFormatterImpl));
			}
			DefaultDefinitionOfFalse = (value) => value != null &&
												  value as bool? != false &&
												  // ReSharper disable once CompareOfFloatsByEqualityOperator
												  value as double? != 0 &&
												  value as int? != 0 &&
												  value as string != string.Empty &&
												  // We've gotten this far, if it is an object that does NOT cast as enumberable, it exists
												  // OR if it IS an enumerable and .Any() returns true, then it exists as well
												  (!(value is IEnumerable) || ((IEnumerable)value).Cast<object>().Any()
												  );
			DefinitionOfFalse = DefaultDefinitionOfFalse;
		}

		/// <summary>
		///		<para>Gets the Default Definition of false.</para>
		///		This is ether:
		///		<para>- Null</para>
		///		<para>- boolean false</para>
		///		<para>- 0 double or int</para>
		///		<para>- string.Empty (whitespaces are allowed)</para>
		///		<para>- collection not Any().</para>
		///		This field can be used to define your own <see cref="DefinitionOfFalse"/> and then fallback to the default logic
		/// </summary>
		public static readonly Func<object, bool> DefaultDefinitionOfFalse;


		/// <summary>
		///		Gets the Definition of false on your Template.
		/// </summary>
		/// <value>
		///		Must no be null
		/// </value>
		/// <exception cref="InvalidOperationException">If the value is null</exception>
		[NotNull]
		public static Func<object, bool> DefinitionOfFalse
		{
			get { return _definitionOfFalse; }
			set
			{
				_definitionOfFalse = value ?? throw new InvalidOperationException("The value must not be null");
			}
		}

		internal static readonly Regex PathFinder = new Regex("[~]{1}|(\\.\\.[\\\\/]{1})|([^.]+)",
			RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

		/// <summary>
		///		Calls <seealso cref="DefaultToStringWithFormatting"/>
		/// </summary>
		/// <param name="sourceValue">The source value.</param>
		/// <param name="formatterArgument">The formatter argument.</param>
		/// <returns></returns>
		public static object DefaultFormatterImpl(object sourceValue, object formatterArgument = null)
		{
			return DefaultToStringWithFormatting(sourceValue, formatterArgument);
		}

		/// <summary>
		///     The default to string operator for any PrintableType.
		///		Can be overwritten to support an alternative formatting of all templates.
		/// </summary>
		[NotNull]
		public static Func<object, object, object> DefaultToStringWithFormatting = (value, formatArgument) =>
		{
			var o = value as IFormattable;
			if (o != null && formatArgument != null)
			{
				return o.ToString(formatArgument.ToString(), null);
			}

			return value.ToString();
		};

		private static Func<object, bool> _definitionOfFalse;

		/// <summary>
		/// Initializes a new instance of the <see cref="ContextObject"/> class.
		/// </summary>
		/// <param name="options">The options.</param>
		/// <param name="key">The key as seen in the Template</param>
		public ContextObject([NotNull]ParserOptions options, [NotNull]string key, [CanBeNull]ContextObject parent)
		{
			Options = options;
			Key = key;
			Parent = parent;
			IsNaturalContext = Parent?.IsNaturalContext ?? true;
		}

		/// <summary>
		///		Gets a value indicating whether this instance is natural context.
		///		A Natural context is a context outside an Isolated scope
		/// </summary>
		protected bool IsNaturalContext { get; set; }

		internal ContextObject FindNextNaturalContextObject()
		{
			if (IsNaturalContext)
			{
				return this;
			}

			return Parent?.FindNextNaturalContextObject();
		}

		/// <summary>
		///     The set of allowed types that may be printed. Complex types (such as arrays and dictionaries)
		///     should not be printed, or their printing should be specialized.
		///     Add an typeof(object) entry as Type to define a Default Output
		/// </summary>
		[NotNull]
		public static IFormatterMatcher DefaultFormatter { get; private set; }

		/// <summary>
		///     The parent of the current context or null if its the root context
		/// </summary>
		[CanBeNull]
		public ContextObject Parent { get; private set; }

		/// <summary>
		///     The evaluated value of the expression
		/// </summary>
		[CanBeNull]
		public object Value { get; set; }

		/// <summary>
		///	Ensures that the Value is loaded if needed
		/// </summary>
		/// <returns></returns>
		public async Task EnsureValue()
		{
			Value = await Value.UnpackFormatterTask();
		}

		/// <summary>
		///     is an abort currently requested
		/// </summary>
		public bool AbortGeneration { get; set; }

		/// <summary>
		///     The name of the property or key inside the value or indexer expression for lists
		/// </summary>
		[NotNull]
		public string Key { get; set; }

		/// <summary>
		///     With what options are the template currently is running
		/// </summary>
		[NotNull]
		public ParserOptions Options { get; }

		/// <summary>
		/// </summary>
		public CancellationToken CancellationToken { get; set; }

		/// <summary>
		///     if overwritten by a class it returns a context object for any non standard key or operation.
		///     if non of that
		///     <value>null</value>
		/// </summary>
		/// <param name="elements"></param>
		/// <param name="currentElement"></param>
		/// <returns></returns>
		protected virtual ContextObject HandlePathContext(Queue<string> elements, string currentElement)
		{
			return null;
		}

		private async Task<ContextObject> GetContextForPath(Queue<string> elements, ScopeData scopeData)
		{
			var retval = this;
			if (elements.Any())
			{
				var path = elements.Dequeue();
				var preHandeld = HandlePathContext(elements, path);
				if (preHandeld != null)
				{
					return preHandeld;
				}

				if (path.StartsWith("~")) //go the root object
				{
					var parent = Parent;
					var lastParent = parent;
					while (parent != null)
					{
						parent = parent.Parent;
						if (parent != null)
						{
							lastParent = parent;
						}
					}

					if (lastParent != null)
					{
						retval = await lastParent.GetContextForPath(elements, scopeData);
					}
				}
				else if (path.Equals("$recursion")) //go the root object
				{
					retval = new ContextObject(Options, path, this)
					{
						Value = scopeData.PartialDepth.Count
					};
				}
				else if (path.StartsWith("..")) //go one level up
				{
					if (Parent != null)
					{
						var parentsRetVal = (await (FindNextNaturalContextObject()?.Parent?.GetContextForPath(elements, scopeData) ?? Task.FromResult((ContextObject)null)));
						if (parentsRetVal != null)
						{
							retval = parentsRetVal;
						}
						else
						{
							retval = await GetContextForPath(elements, scopeData);
						}
					}
					else
					{
						retval = await GetContextForPath(elements, scopeData);
					}
				}
				else
				{
					await EnsureValue();
					var type = Value?.GetType();
					if (path.StartsWith("?")) //enumerate ether an IDictionary, an cs object or an IEnumerable to a KeyValuePair array
					{
						//ALWAYS return the context, even if the value is null.
						var innerContext = new ContextObject(Options, path, this);
						switch (Value)
						{
							case IDictionary<string, object> dictList:
								innerContext.Value = dictList.Select(e => e);
								break;
							//This is a draft that i have discarded as its more likely to enumerate a single IEnumerable with #each alone
							//case IEnumerable ctx:
							//	innerContext.Value = ctx.OfType<object>().Select((item, index) => new KeyValuePair<string, object>(index.ToString(), item));
							//	break;
							default:
								{
									if (Value != null)
									{
										innerContext.Value = type
											.GetTypeInfo()
											.GetProperties(BindingFlags.Instance | BindingFlags.Public)
											.Where(e => !e.IsSpecialName && !e.GetIndexParameters().Any())
											.Select(e => new KeyValuePair<string, object>(e.Name, e.GetValue(Value)));
									}

									break;
								}
						}

						retval = await innerContext.GetContextForPath(elements, scopeData);
					}
					//TODO: handle array accessors and maybe "special" keys.
					else
					{
						//ALWAYS return the context, even if the value is null.
						var innerContext = new ContextObject(Options, path, this);
						if (Options.ValueResolver?.CanResolve(type, Value, path, innerContext) == true)
						{
							innerContext.Value = Options.ValueResolver.Resolve(type, Value, path, innerContext);
						}
						else if (Value is IDictionary<string, object> ctx)
						{
							if (!ctx.TryGetValue(path, out var o))
							{
								Options.OnUnresolvedPath(path, type);
							}
							innerContext.Value = o;
						}
						else if (Value != null)
						{
							var propertyInfo = type.GetTypeInfo().GetProperty(path);
							if (propertyInfo != null)
							{
								innerContext.Value = propertyInfo.GetValue(Value);
							}
							else
							{
								Options.OnUnresolvedPath(path, type);
							}
						}

						retval = await innerContext.GetContextForPath(elements, scopeData);
					}
				}
			}

			return retval;
		}

		/// <summary>
		///     Will walk the path by using the path seperator "." and evaluate the object at the end
		/// </summary>
		/// <param name="path"></param>
		/// <param name="scopeData"></param>
		/// <returns></returns>
		internal async Task<ContextObject> GetContextForPath(string path, ScopeData scopeData)
		{
			var elements = new Queue<string>();
			foreach (var m in PathFinder.Matches(path).OfType<Match>())
			{
				elements.Enqueue(m.Value);
			}

			if (elements.Any())
			{
				//look at the first element if its an alias switch to that alias
				var maybeAlias = elements.Peek();
				if (scopeData.Alias.TryGetValue(maybeAlias, out var alias))
				{
					elements.Dequeue();
					return await alias.GetContextForPath(elements, scopeData);
				}
			}

			return await GetContextForPath(elements, scopeData);
		}

		/// <summary>
		///     Determines if the value of this context exists.
		/// </summary>
		/// <returns></returns>
		public async Task<bool> Exists()
		{
			await EnsureValue();
			return DefinitionOfFalse(Value);
		}

		/// <summary>
		///		Renders the Current value to a string or if null to the Null placeholder in the Options
		/// </summary>
		/// <returns></returns>
		public async Task<string> RenderToString()
		{
			await EnsureValue();
			return Value?.ToString() ?? Options.Null;
		}

		/// <summary>
		///     Parses the current object by using the given argument
		/// </summary>
		/// <param name="argument"></param>
		/// <returns></returns>
		public async Task<object> Format(KeyValuePair<string, object>[] argument)
		{
			await EnsureValue();
			var retval = Value;
			if (Value == null)
			{
				return retval;
			}

			//call formatters that are given by the Options for this run
			retval = await Options.Formatters.CallMostMatchingFormatter(Value.GetType(), argument, Value);
			if ((retval as FormatterMatcher.FormatterFlow) != FormatterMatcher.FormatterFlow.Skip)
			{
				//one formatter has returned a valid value so use this one.
				return retval;
			}

			//all formatters in the options object have rejected the value so try use the global ones
			retval = await DefaultFormatter.CallMostMatchingFormatter(Value.GetType(), argument, Value);
			if ((retval as FormatterMatcher.FormatterFlow) != FormatterMatcher.FormatterFlow.Skip)
			{
				return retval;
			}
			return Value;
		}

		/// <summary>
		///     Clones the ContextObject into a new Detached object
		/// </summary>
		/// <returns></returns>
		public virtual ContextObject Clone()
		{
			var contextClone = new ContextObject(Options, Key, this) //note: Parent must be the original context so we can traverse up to an unmodified context
			{
				CancellationToken = CancellationToken,
				AbortGeneration = AbortGeneration,
				Value = Value,
				IsNaturalContext = false
			};

			return contextClone;
		}
	}
}