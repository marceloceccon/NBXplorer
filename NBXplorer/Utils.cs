﻿using NBXplorer.Backend;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NBXplorer
{
	public static class Utils
	{
		public static ICollection<AnnotatedTransaction> TopologicalSort(this ICollection<AnnotatedTransaction> transactions)
		{
			var confirmed = new MultiValueDictionary<long, AnnotatedTransaction>();
			var unconfirmed = new List<AnnotatedTransaction>();
			var result = new List<AnnotatedTransaction>(transactions.Count);
			foreach (var tx in transactions)
			{
				if (tx.Height is long h)
					confirmed.Add(h, tx);
				else
					unconfirmed.Add(tx);
			}
			foreach (var tx in confirmed.OrderBy(o => o.Key))
			{
				if (tx.Value.Count == 1)
					result.Add(tx.Value.First());
				else
				{
					foreach (var tx2 in tx.Value.TopologicalSortCore())
					{
						result.Add(tx2);
					}
				}
			}
			foreach (var tx in unconfirmed.TopologicalSortCore())
			{
				result.Add(tx);
			}
			return result;
		}

		static ICollection<AnnotatedTransaction> TopologicalSortCore(this IReadOnlyCollection<AnnotatedTransaction> transactions)
		{
			return transactions.TopologicalSort(
			   dependsOn: t => t.Record.SpentOutpoints.Select(o => o.Outpoint.Hash),
			   getKey: t => t.Record.TransactionHash,
			   getValue: t => t,
			   solveTies: AnnotatedTransactionComparer.OldToYoung);
		}

		public static List<T> TopologicalSort<T>(this IReadOnlyCollection<T> nodes, Func<T, IEnumerable<T>> dependsOn)
		{
			return nodes.TopologicalSort(dependsOn, k => k, k => k);
		}

		public static List<T> TopologicalSort<T, TDepend>(this IReadOnlyCollection<T> nodes, Func<T, IEnumerable<TDepend>> dependsOn, Func<T, TDepend> getKey)
		{
			return nodes.TopologicalSort(dependsOn, getKey, o => o);
		}

		public static List<TValue> TopologicalSort<T, TDepend, TValue>(this IReadOnlyCollection<T> nodes,
									  Func<T, IEnumerable<TDepend>> dependsOn,
									  Func<T, TDepend> getKey,
									  Func<T, TValue> getValue,
									  IComparer<T> solveTies = null)
		{
			if (nodes.Count == 0)
				return new List<TValue>();
			if (getKey == null)
				throw new ArgumentNullException(nameof(getKey));
			if (getValue == null)
				throw new ArgumentNullException(nameof(getValue));
			solveTies = solveTies ?? Comparer<T>.Default;
			List<TValue> result = new List<TValue>(nodes.Count);
			HashSet<TDepend> allKeys = new HashSet<TDepend>(nodes.Count);
			var noDependencies = new SortedDictionary<T, HashSet<TDepend>>(solveTies);

			foreach (var node in nodes)
				allKeys.Add(getKey(node));
			var dependenciesByValues = nodes.ToDictionary(node => node,
									node => new HashSet<TDepend>(dependsOn(node).Where(n => allKeys.Contains(n))));
			foreach (var e in dependenciesByValues.Where(x => x.Value.Count == 0))
			{
				noDependencies.Add(e.Key, e.Value);
			}
			if (noDependencies.Count == 0)
			{
				throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
			}
			while (noDependencies.Count > 0)
			{
				var nodep = noDependencies.First();
				noDependencies.Remove(nodep.Key);
				dependenciesByValues.Remove(nodep.Key);

				var elemKey = getKey(nodep.Key);
				result.Add(getValue(nodep.Key));
				foreach (var selem in dependenciesByValues)
				{
					if (selem.Value.Remove(elemKey) && selem.Value.Count == 0)
						noDependencies.Add(selem.Key, selem.Value);
				}
			}
			if (dependenciesByValues.Count != 0)
			{
				throw new InvalidOperationException("Impossible to topologically sort a cyclic graph");
			}
			return result;
		}

		public static TransactionResult ToTransactionResult(long height, SavedTransaction result)
		=> new TransactionResult()
			{
				Confirmations = result.BlockHeight is long bh ? height - bh + 1 : 0,
				BlockId = result.BlockHash,
				Transaction = result.Transaction,
				TransactionHash = result.TxId,
				Height = result.BlockHeight,
				Timestamp = result.Timestamp,
				ReplacedBy = result.ReplacedBy,
				Metadata = result.Metadata
			};
	}
}
