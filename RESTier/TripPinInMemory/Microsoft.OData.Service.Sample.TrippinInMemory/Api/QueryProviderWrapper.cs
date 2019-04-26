using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Web;

namespace Microsoft.OData.Service.Sample.TrippinInMemory.Api {
    public class QueryProviderWrapper : IQueryProvider {
        volatile static MethodInfo genericCreateQueryMethod;
        readonly IQueryProvider queryProvider;
        public QueryProviderWrapper(IQueryProvider queryProvider) {
            this.queryProvider = queryProvider;
        }
        public static MethodInfo GenericCreateQueryMethod {
            get {
                if(genericCreateQueryMethod == null) {
                   genericCreateQueryMethod = typeof(QueryProviderWrapper).GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => m.Name == "CreateQuery" && m.IsGenericMethodDefinition).Single();
                }
                return genericCreateQueryMethod;
            }
        }
        public IQueryProvider QueryProvider => queryProvider;
        public IQueryable CreateQuery(Expression expression) {
            var visitor = new QueryableWrapperExpressionVisitor();
            visitor.Visit(expression);
            if(visitor.LastElementType != null) {
                var elementType = visitor.LastElementType;
                Type typeDefinition;
                if(expression.Type.IsGenericType && ((typeDefinition = expression.Type.GetGenericTypeDefinition()) == typeof(IQueryable<>) || typeDefinition == typeof(IOrderedQueryable<>) || typeDefinition == typeof(QueryableWrapper<>) || typeDefinition == typeof(EnumerableQuery<>))) {
                    elementType = expression.Type.GetGenericArguments()[0];
                }
                var createQueryMethod = GenericCreateQueryMethod.MakeGenericMethod(elementType);
                return (IQueryable)createQueryMethod.Invoke(this, new object[] { expression });
            }
            return new QueryableWrapper(queryProvider.CreateQuery(expression));
        }
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression) {
            var query = queryProvider.CreateQuery<TElement>(expression);
            var orderedQuery = query as IOrderedQueryable<TElement>;
            return orderedQuery == null ? new QueryableWrapper<TElement>(query) : new QueryableWrapperOrdered<TElement>(orderedQuery);
        }
        public object Execute(Expression expression) {
            return queryProvider.Execute(new QueryableWrapperReverseExpressionVisitor().Visit(expression));
        }
        public TResult Execute<TResult>(Expression expression) {
            return queryProvider.Execute<TResult>(new QueryableWrapperReverseExpressionVisitor().Visit(expression));
        }
    }
    public class QueryableWrapperReverseExpressionVisitor : ExpressionVisitor {
        protected override Expression VisitConstant(ConstantExpression node) {
            var nested = base.VisitConstant(node);
            var nestedConstant = nested as ConstantExpression;
            if(nestedConstant != null && nestedConstant.Value != null) {
                Type nodeType = nestedConstant.Value.GetType();
                Type typeDefinition;
                if(nodeType.IsGenericType && nestedConstant.NodeType == ExpressionType.Constant && ((typeDefinition = nodeType.GetGenericTypeDefinition()) == typeof(QueryableWrapper<>) || typeDefinition == typeof(QueryableWrapperOrdered<>))) {
                    Type elementType = nodeType.GetGenericArguments()[0];
                    var originalQuery = ((IQueryableWrapper)((ConstantExpression)nestedConstant).Value).OriginalQuery;
                    return Expression.Constant(originalQuery, typeof(IQueryable<>).MakeGenericType(elementType));
                }
            }
            return nestedConstant;
        }
    }
    public class QueryableWrapperExpressionVisitor : ExpressionVisitor {
        Type lastElementType;
        public Type LastElementType {
            get { return lastElementType; }
        }
        protected override Expression VisitConstant(ConstantExpression node) {
            var nestedConstant = base.VisitConstant(node);
            Type nodeType = nestedConstant.Type;
            Type typeDefinition;
            if(nodeType.IsGenericType && ((typeDefinition = nodeType.GetGenericTypeDefinition()) == typeof(EnumerableQuery<>) || typeDefinition == typeof(QueryableWrapper<>) || typeDefinition == typeof(IQueryable<>))) {
                Type elementType = nodeType.GetGenericArguments()[0];
                lastElementType = elementType;
                if(nestedConstant.NodeType == ExpressionType.Constant && typeDefinition == typeof(EnumerableQuery<>)) {
                    Type wrapperType = typeof(QueryableWrapper<>).MakeGenericType(elementType);
                    return Expression.Constant(Activator.CreateInstance(wrapperType, ((ConstantExpression)nestedConstant).Value), typeof(IQueryable<>).MakeGenericType(elementType));
                }
            }
            return nestedConstant;
        }
    }
    public interface IQueryableWrapper {
        IQueryable OriginalQuery { get; }
    }
    public class QueryableWrapperOrdered<T> : QueryableWrapper<T>, IOrderedQueryable<T>{
        public QueryableWrapperOrdered(IOrderedQueryable<T> query)
            : base(query) {
        }
    }
    public class QueryableWrapper<T> : IQueryable<T>, IQueryableWrapper {
        readonly IQueryable<T> query;
        public QueryableWrapper(IQueryable<T> query) {
            this.query = query;
        }
        public Expression Expression {
            get { return new QueryableWrapperExpressionVisitor().Visit(query.Expression); }
        }
        public Type ElementType {
            get { return query.ElementType; }
        }
        public IQueryProvider Provider {
            get { return new QueryProviderWrapper(query.Provider); }
        }

        public IQueryable OriginalQuery => query;

        public IEnumerator<T> GetEnumerator() {
            return query.Provider.CreateQuery<T>(new QueryableWrapperReverseExpressionVisitor().Visit(query.Expression)).GetEnumerator();
        }
        IEnumerator IEnumerable.GetEnumerator() {
            return query.Provider.CreateQuery(new QueryableWrapperReverseExpressionVisitor().Visit(query.Expression)).GetEnumerator();
        }
    }

    public class QueryableWrapper : IQueryable {
        readonly IQueryable query;
        public QueryableWrapper(IQueryable query) {
            this.query = query;
        }
        public Expression Expression {
            get { return query.Expression; }
        }
        public Type ElementType {
            get { return query.ElementType; }
        }
        public IQueryProvider Provider {
            get { return new QueryProviderWrapper(query.Provider); }
        }
        IEnumerator IEnumerable.GetEnumerator() {
            return ((IEnumerable)query).GetEnumerator();
        }
    }
}