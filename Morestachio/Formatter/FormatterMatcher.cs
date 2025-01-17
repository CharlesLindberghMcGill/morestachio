﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Morestachio.Attributes;
using Morestachio.Helper;

namespace Morestachio.Formatter
{
	/// <summary>
	///     Matches the Arguments from the Template to a Function from .net
	/// </summary>
	public class FormatterMatcher : IFormatterMatcher
	{
		/// <summary>
		/// </summary>
		public FormatterMatcher()
		{
			Formatter = new List<FormatTemplateElement>();
		}

		/// <summary>
		///     If set to <code>true</code> this Formatter will search for existing formatter for the given type and if found any,
		///     replace
		///     them with the new one.
		///     Default: <code>False</code>
		/// </summary>
		public bool ReplaceExisting { get; set; }

		/// <summary>
		///     If set writes the Formatters log.
		/// </summary>
		[CanBeNull]
		public TextWriter FormatterLog { get; set; }

		/// <summary>
		///     The Enumeration of all formatter
		/// </summary>
		[NotNull]
		[ItemNotNull]
		public ICollection<FormatTemplateElement> Formatter { get; }

		/// <summary>
		///     Adds the formatter.
		/// </summary>
		/// <typeparam name="T"></typeparam>
		/// <param name="formatterDelegate">The formatter delegate.</param>
		public virtual FormatTemplateElement AddFormatter<T>(Delegate formatterDelegate)
		{
			return AddFormatter(typeof(T), formatterDelegate);
		}

		/// <summary>
		///     Adds the formatter.
		/// </summary>
		/// <param name="formatter">The formatter.</param>
		public virtual FormatTemplateElement AddFormatter(FormatTemplateElement formatter)
		{
			if (ReplaceExisting)
			{
				Formatter.Remove(Formatter.FirstOrDefault(e => e.InputTypes == formatter.InputTypes));
			}

			Formatter.Add(formatter);
			return formatter;
		}

		/// <summary>
		///     Adds the formatter.
		/// </summary>
		/// <param name="forType">For type.</param>
		/// <param name="formatterDelegate">The formatter delegate.</param>
		public virtual FormatTemplateElement AddFormatter(Type forType, Delegate formatterDelegate)
		{
			var arguments = formatterDelegate.GetMethodInfo().GetParameters().Select((e, index) =>
				new MultiFormatterInfo(
					e.ParameterType,
					e.GetCustomAttribute<FormatterArgumentNameAttribute>()?.Name ?? e.Name,
					e.IsOptional,
					index,
					e.GetCustomAttribute<ParamArrayAttribute>() != null ||
					e.GetCustomAttribute<RestParameterAttribute>() != null)
				{
					IsSourceObject = e.GetCustomAttribute<SourceObjectAttribute>() != null
				}).ToArray();

			var returnValue = formatterDelegate.GetMethodInfo().ReturnParameter?.ParameterType;

			//if there is no declared SourceObject then check if the first object is of type what we are formatting and use this one.
			if (!arguments.Any(e => e.IsSourceObject) && arguments.Any() &&
			    arguments[0].Type.GetTypeInfo().IsAssignableFrom(forType))
			{
				arguments[0].IsSourceObject = true;
			}

			var sourceValue = arguments.FirstOrDefault(e => e.IsSourceObject);
			if (sourceValue != null)
			{
				//if we have a source value in the arguments reduce the index of all following 
				//this is important as the source value is never in the formatter string so we will not "count" it 
				for (var i = sourceValue.Index; i < arguments.Length; i++)
				{
					arguments[i].Index--;
				}

				sourceValue.Index = -1;
			}

			return AddFormatter(new FormatTemplateElement(
				formatterDelegate,
				forType,
				returnValue,
				arguments));
		}

		/// <summary>
		///     Executes the specified formatter.
		/// </summary>
		/// <param name="formatter">The formatter.</param>
		/// <param name="sourceObject">The source object.</param>
		/// <param name="templateArguments">The template arguments.</param>
		/// <returns></returns>
		public virtual async Task<object> Execute(FormatTemplateElement formatter,
			object sourceObject,
			params KeyValuePair<string, object>[] templateArguments)
		{
			var values = ComposeValues(formatter, sourceObject, templateArguments);

			if (values == null)
			{
				Log(() => "Skip: Execute skip as Compose Values returned an invalid value");
				return FormatterFlow.Skip;
			}

			if (formatter.CanFormat != null)
			{
				if (!formatter.CanFormat(sourceObject, templateArguments))
				{
					Log(() => "Skip: Execute skip as CanExecute is false.");
					return FormatterFlow.Skip;
				}
			}

			Log(() => "Execute");
			var taskAlike = formatter.Format.DynamicInvoke(values.Select(e => e.Value).ToArray());

			return await taskAlike.UnpackFormatterTask();
		}

		/// <summary>
		///     Gets the Formatter that matches the type or is assignable to that type. If null it will search for a object
		///     formatter
		/// </summary>
		/// <param name="type"></param>
		/// <param name="arguments"></param>
		/// <returns></returns>
		public IEnumerable<FormatTemplateElement> GetMostMatchingFormatter(Type type,
			KeyValuePair<string, object>[] arguments)
		{
			if (type == null)
			{
				return GetMatchingFormatter(typeof(object), arguments);
			}

			return GetMatchingFormatter(type, arguments);
		}

		/// <summary>
		///     Searches for the first formatter does not reject the value.
		/// </summary>
		/// <param name="type">The type.</param>
		/// <param name="arguments">The arguments.</param>
		/// <param name="value">The value.</param>
		/// <returns></returns>
		public async Task<object> CallMostMatchingFormatter(Type type, KeyValuePair<string, object>[] arguments,
			object value)
		{
			Log(() => "---------------------------------------------------------------------------------------------");
			Log(() => $"Call Formatter for Type '{type}' on '{value}'");
			var hasFormatter = GetMostMatchingFormatter(type, arguments).Where(e => e != null);

			foreach (var formatTemplateElement in hasFormatter)
			{
				Log(() =>
					$"Try formatter '{formatTemplateElement.InputTypes}' on '{formatTemplateElement.Format.GetMethodInfo().Name}'");
				var executeFormatter = await Execute(formatTemplateElement, value, arguments);
				if (executeFormatter as FormatterFlow != FormatterFlow.Skip)
				{
					Log(() => $"Success. return object {executeFormatter}");
					return executeFormatter;
				}

				Log(() => $"Formatter returned '{executeFormatter}'. Try another");
			}

			Log(() => "No Formatter has matched. Skip and return Source Value.");

			return FormatterFlow.Skip;
		}

		/// <summary>
		///     Writes the specified log.
		/// </summary>
		/// <param name="log">The log.</param>
		public void Log(Func<string> log)
		{
			FormatterLog?.WriteLine(log());
		}

		/// <summary>
		///     Gets the matching formatter.
		/// </summary>
		/// <param name="typeToFormat">The type to format.</param>
		/// <param name="arguments"></param>
		/// <returns></returns>
		[CanBeNull]
		public virtual IEnumerable<FormatTemplateElement> GetMatchingFormatter([NotNull] Type typeToFormat,
			[NotNull] KeyValuePair<string, object>[] arguments)
		{
			Log(() =>
			{
				var aggregate = arguments.Any() ? arguments.Select(e => $"[{e.Key}]:\"{e.Value}\"").Aggregate((e, f) => e + " & " + f) : "";
				return
					$"Test Filter for '{typeToFormat}' with arguments '{aggregate}'";
			});

			var filteredSourceList = new List<KeyValuePair<FormatTemplateElement, ulong>>();
			foreach (var formatTemplateElement in Formatter)
			{
				var formatter = formatTemplateElement;

				Log(() => $"Test filter: '{formatter.InputTypes} : {formatter.Format.GetMethodInfo().Name}'");

				if (formatTemplateElement.InputTypes != typeToFormat &&
				    !formatTemplateElement.InputTypes.GetTypeInfo().IsAssignableFrom(typeToFormat))
				{
					var typeToFormatGenerics = typeToFormat.GetTypeInfo().GetGenericArguments();

					//explicit check for array support
					if (typeToFormat.HasElementType)
					{
						var elementType = typeToFormat.GetElementType();
						typeToFormatGenerics = typeToFormatGenerics.Concat(new[] {elementType}).ToArray();
					}

					//the type check has maybe failed because of generic parameter. Check if both the formatter and the typ have generic arguments

					var formatterGenerics = formatTemplateElement.InputTypes.GetTypeInfo().GetGenericArguments();

					if (typeToFormatGenerics.Length <= 0 || formatterGenerics.Length <= 0 ||
					    typeToFormatGenerics.Length != formatterGenerics.Length)
					{
						Log(() =>
							$"Exclude because formatter accepts '{formatTemplateElement.InputTypes}' is not assignable from '{typeToFormat}'");
						continue;
					}
				}

				//count rest arguments
				var mandatoryArguments = formatter.MetaData
					.Where(e => !e.IsRestObject && !e.IsOptional && !e.IsSourceObject).ToArray();
				if (mandatoryArguments.Length > arguments.Length)
					//if there are less arguments excluding rest then parameters
				{
					Log(() =>
						"Exclude because formatter has " +
						$"'{mandatoryArguments.Length}' " +
						"parameter and " +
						$"'{formatter.MetaData.Count(e => e.IsRestObject)}' " +
						"rest parameter but needs less or equals" +
						$"'{arguments.Length}'.");
					continue;
				}

				ulong score = 1L;
				if (formatter.Format.GetMethodInfo().ReturnParameter == null ||
				    formatter.Format.GetMethodInfo().ReturnParameter?.ParameterType == typeof(void))
				{
					score++;
				}

				score += (ulong) (arguments.Length - mandatoryArguments.Length);
				Log(() => $"Take filter: '{formatter.InputTypes} : {formatter.Format}' Score {score}");
				filteredSourceList.Add(new KeyValuePair<FormatTemplateElement, ulong>(formatter, score));
			}

			foreach (var formatTemplateElement in filteredSourceList.OrderBy(e => e.Value))
			{
				yield return formatTemplateElement.Key;
			}
		}

		/// <summary>
		///     Composes the values into a Dictionary for each formatter. If returns null, the formatter will not be called.
		/// </summary>
		/// <param name="formatter">The formatter.</param>
		/// <param name="sourceObject">The source object.</param>
		/// <param name="templateArguments">The template arguments.</param>
		/// <returns></returns>
		public virtual IDictionary<MultiFormatterInfo, object> ComposeValues([NotNull] FormatTemplateElement formatter,
			[CanBeNull] object sourceObject, [NotNull] params KeyValuePair<string, object>[] templateArguments)
		{
			Log(() =>
				$"Compose values for object '{sourceObject}' with formatter '{formatter.InputTypes}' targets '{formatter.Format.GetMethodInfo().Name}'");
			var values = new Dictionary<MultiFormatterInfo, object>();
			var matched = new Dictionary<MultiFormatterInfo, KeyValuePair<string, object>>();

			var argumentIndex = 0;
			foreach (var multiFormatterInfo in formatter.MetaData.Where(e => !e.IsRestObject))
			{
				argumentIndex++;
				Log(() => $"Match parameter '{multiFormatterInfo.Type}' [{multiFormatterInfo.Name}]");
				object givenValue;
				//set ether the source object or the value from the given arguments
				if (multiFormatterInfo.IsSourceObject)
				{
					Log(() => "Is Source object");
					givenValue = sourceObject;
				}
				else
				{
					//match by index or name
					Log(() => "Match by Name");
					//match by name
					var match = templateArguments.FirstOrDefault(e =>
						!string.IsNullOrWhiteSpace(e.Key) && e.Key.Equals(multiFormatterInfo.Name));

					if (default(KeyValuePair<string, object>).Equals(match))
					{
						Log(() => "Match by Index");
						//match by index
						var index = 0;
						match = templateArguments.FirstOrDefault(g => index++ == multiFormatterInfo.Index);
					}

					givenValue = match.Value;
					Log(() => $"Matched '{match.Key}': '{match.Value}' by Name/Index");

					//check for matching types
					if (!multiFormatterInfo.Type.GetTypeInfo().IsInstanceOfType(match.Value))
					{
						Log(() =>
							$"Skip: Match is Invalid because type at {argumentIndex} of '{multiFormatterInfo.Type.Name}' was not expected. Abort.");
						//The type in the template and the type defined in the formatter do not match. Abort
						return null;
					}

					matched.Add(multiFormatterInfo, match);
				}

				values.Add(multiFormatterInfo, givenValue);
				if (multiFormatterInfo.IsOptional || multiFormatterInfo.IsSourceObject)
				{
					continue; //value and source object are optional so we do not to check for its existence 
				}

				if (Equals(givenValue, null))
				{
					Log(() =>
						"Skip: Match is Invalid because template value is null where the Formatter does not have a optional value");
					//the delegates parameter is not optional so this formatter does not fit. Continue.
					return null;
				}
			}

			var hasRest = formatter.MetaData.FirstOrDefault(e => e.IsRestObject);
			if (hasRest == null)
			{
				return values;
			}

			Log(() => $"Match Rest argument '{hasRest.Type}'");

			//only use the values that are not matched.
			var restValues = templateArguments.Except(matched.Values);

			if (typeof(KeyValuePair<string, object>[]) == hasRest.Type)
			{
				//keep the name value pairs
				values.Add(hasRest, restValues);
			}
			else if (typeof(object[]).GetTypeInfo().IsAssignableFrom(hasRest.Type))
			{
				//its requested to transform the rest values and truncate the names from it.
				values.Add(hasRest, restValues.Select(e => e.Value).ToArray());
			}
			else
			{
				Log(() => $"Skip: Match is Invalid because  '{hasRest.Type}' is no supported rest parameter");
				//unknown type in params argument cannot call
				return null;
			}

			return values;
		}

		/// <summary>
		///     Can be returned by a Formatter to control what formatter should be used
		/// </summary>
		public class FormatterFlow
		{
			private FormatterFlow()
			{
			}

			/// <summary>
			///     Return code for all formatters to skip the execution of the current formatter and try another one that could also
			///     match
			/// </summary>
			public static FormatterFlow Skip { get; } = new FormatterFlow();
		}
	}
}