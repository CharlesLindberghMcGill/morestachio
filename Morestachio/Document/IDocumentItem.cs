﻿using System.Collections.Generic;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Morestachio.Framework;

namespace Morestachio
{
	/// <summary>
	///		Defines a Part in the Template that provides a direct access to 
	/// </summary>
	public interface IValueDocumentItem
	{
		/// <summary>
		///		Traverses the path down
		/// </summary>
		Task<ContextObject> GetValue(ContextObject context,
			ScopeData scopeData);
	}

	/// <summary>
	///		Defines a Part in the Template that can be processed
	/// </summary>
	public interface IDocumentItem
	{
		/// <summary>
		///		Renders its Value into the <see cref="outputStream"/>.
		///		If there are any Document items that should be executed directly after they should be returned		
		/// </summary>
		/// <param name="outputStream">The output stream.</param>
		/// <param name="context">The context.</param>
		/// <param name="scopeData">The scope data.</param>
		/// <returns></returns>
		Task<IEnumerable<DocumentItemExecution>> Render(IByteCounterStream outputStream, ContextObject context, ScopeData scopeData);

		/// <summary>
		///		Gets the Kind of this Document item
		/// </summary>
		[PublicAPI]
		string Kind { get; }

		/// <summary>
		///		The list of Children that are children of this Document item
		/// </summary>
		IList<IDocumentItem> Children { get; }

		/// <summary>
		///		Adds the specified childs.
		/// </summary>
		void Add(params IDocumentItem[] documentChildren);

		/// <summary>
		///		If this is a Natural Document item this defines the Position within the Template where the DocumentItem is parsed from
		/// </summary>
		Tokenizer.CharacterLocation ExpressionStart { get; set; }
	}
}