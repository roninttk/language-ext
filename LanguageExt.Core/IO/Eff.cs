using System;
using LanguageExt.Common;
using System.Threading.Tasks;
using static LanguageExt.Prelude;
using System.Diagnostics.Contracts;
using System.Runtime.CompilerServices;
using LanguageExt.Interfaces;
using LanguageExt.Thunks;

namespace LanguageExt
{
    /// <summary>
    /// Synchronous IO monad
    /// </summary>
    public struct Eff<Env, A>
    {
        internal Thunk<Env, A> thunk;

        /// <summary>
        /// Constructor
        /// </summary>
        [MethodImpl(AffOpt.mops)]
        internal Eff(Thunk<Env, A> thunk) =>
            this.thunk = thunk ?? throw new ArgumentNullException(nameof(thunk));

        /// <summary>
        /// Invoke the effect
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public Fin<A> RunIO(Env env) =>
            thunk.Value(env);

        /// <summary>
        /// Invoke the effect
        /// </summary>
        [MethodImpl(AffOpt.mops)]
        public Unit RunUnitIO(Env env) =>
            ignore(thunk.Value(env));

        /// <summary>
        /// Lift a synchronous effect into the IO monad
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> EffectMaybe(Func<Env, Fin<A>> f) =>
            new Eff<Env, A>(Thunk<Env, A>.Lazy(f));

        /// <summary>
        /// Lift a synchronous effect into the IO monad
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Effect(Func<Env, A> f) =>
            new Eff<Env, A>(Thunk<Env, A>.Lazy(e => Fin<A>.Succ(f(e))));

        /// <summary>
        /// Lift a value into the IO monad 
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Success(A value) =>
            new Eff<Env, A>(Thunk<Env, A>.Success(value));

        /// <summary>
        /// Lift a failure into the IO monad 
        /// </summary>
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Fail(Error error) =>
            new Eff<Env, A>(Thunk<Env, A>.Fail(error));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> operator |(Eff<Env, A> ma, Eff<Env, A> mb) =>
            new Eff<Env, A>(Thunk<Env, A>.Lazy(
                env =>
                {
                    var ra = ma.RunIO(env);
                    return ra.IsSucc
                        ? ra
                        : mb.RunIO(env);
                }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> operator |(Eff<Env, A> ma, LanguageExt.Eff<A> mb) =>
            new Eff<Env, A>(Thunk<Env, A>.Lazy(
                e =>
                {
                    var ra = ma.RunIO(e);
                    return ra.IsSucc
                        ? ra
                        : mb.RunIO();
                }));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> operator |(LanguageExt.Eff<A> ma, Eff<Env, A> mb) =>
            new Eff<Env, A>(Thunk<Env, A>.Lazy(
                e =>
                {
                    var ra = ma.RunIO();
                    return ra.IsSucc
                        ? ra
                        : mb.RunIO(e);
                }));


        /// <summary>
        /// Clear the memoised value
        /// </summary>
        [MethodImpl(AffOpt.mops)]
        public Unit Clear()
        {
            thunk = thunk.Clone();
            return default;
        }

        /// <summary>
        /// Implicit conversion from pure Eff
        /// </summary>
        public static implicit operator Eff<Env, A>(Eff<A> ma) =>
            EffectMaybe(env => ma.RunIO());
    }

    public static partial class AffExtensions
    {
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> ToAsync<Env, A>(this Eff<Env, A> ma) where Env : struct, HasCancel<Env> =>
            Aff<Env, A>.EffectMaybe(e => new ValueTask<Fin<A>>(ma.RunIO(e)));

        // Map and map-left
 
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Map<Env, A, B>(this Eff<Env, A> ma, Func<A, B> f) =>
            new Eff<Env, B>(ma.thunk.Map(f));

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> MapFail<Env, A>(this Eff<Env, A> ma, Func<Error, Error> f) =>
            ma.BiMap(identity, f);
        
        //
        // Bi-map
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> BiMap<Env, A, B>(this Eff<Env, A> ma, Func<A, B> Succ, Func<Error, Error> Fail) =>
            new Eff<Env, B>(ma.thunk.BiMap(Succ, Fail));

        //
        // Match
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, Func<A, B> Succ, Func<Error, B> Fail) where Env : struct, HasCancel<Env> =>
            Eff<Env, B>(env => { 
            
                var r = ma.RunIO(env);
                return r.IsSucc
                    ? Succ(r.Value)
                    : Fail(r.Error);
            });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, Func<A, B> Succ, Aff<Env, B> Fail) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, B>(async env =>
            {
                var r = ma.RunIO(env);
                return r.IsSucc
                    ? Succ(r.Value)
                    : await Fail.RunIO(env).ConfigureAwait(false);
            });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, Func<A, B> Succ, Eff<Env, B> Fail) =>
            EffMaybe<Env, B>(env =>
            {
                var r = ma.RunIO(env);
                return r.IsSucc
                    ? Succ(r.Value)
                    : Fail.RunIO(env);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, Aff<Env, B> Succ, Func<Error, B> Fail) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, B>(async env =>
            {
                var r = ma.RunIO(env);
                return r.IsSucc
                    ? await Succ.RunIO(env).ConfigureAwait(false)
                    : Fail(r.Error);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, Eff<Env, B> Succ, Func<Error, B> Fail) =>
            EffMaybe<Env, B>(env =>
            {
                var r = ma.RunIO(env);
                return r.IsSucc
                    ? Succ.RunIO(env)
                    : Fail(r.Error);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, Aff<Env, B> Succ, Aff<Env, B> Fail) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, B>(async env =>
            {
                var r = ma.RunIO(env);
                return r.IsSucc
                    ? await Succ.RunIO(env).ConfigureAwait(false)
                    : await Fail.RunIO(env).ConfigureAwait(false);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, Eff<Env, B> Succ, Eff<Env, B> Fail) =>
            EffMaybe<Env, B>(env =>
            {
                var r = ma.RunIO(env);
                return r.IsSucc
                    ? Succ.RunIO(env)
                    : Fail.RunIO(env);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, B Succ, Func<Error, B> Fail) =>
            Eff<Env, B>(env =>
            {
                var r = ma.RunIO(env);
                return r.IsSucc
                    ? Succ
                    : Fail(r.Error);
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, Func<A, B> Succ, B Fail) =>
            Eff<Env, B>(env =>
            {
                var r = ma.RunIO(env);
                return r.IsSucc
                           ? Succ(r.Value)
                           : Fail;
            });

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Match<Env, A, B>(this Eff<Env, A> ma, B Succ, B Fail) =>
            Eff<Env, B>(env =>
            {
                var r = ma.RunIO(env);
                return r.IsSucc
                           ? Succ
                           : Fail;
            });
        
        //
        // IfNone
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> IfFail<Env, A>(this Eff<Env, A> ma, Func<Error, A> f) =>
            EffMaybe<Env, A>(
                env =>
                {
                    var res = ma.RunIO(env);
                    return res.IsSucc
                               ? res
                               : Fin<A>.Succ(f(res.Error));
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> IfFail<Env, A>(this Eff<Env, A> ma, A alternative) =>
            EffMaybe<Env, A>(
                env =>
                {
                    var res = ma.RunIO(env);
                    return res.IsSucc
                               ? res
                               : Fin<A>.Succ(alternative);
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> IfFail<Env, A>(this Eff<Env, A> ma, Aff<Env, A> alternative) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = ma.RunIO(env);
                    return res.IsSucc
                               ? res
                               : await alternative.RunIO(env).ConfigureAwait(false);
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> IfFail<Env, A>(this Eff<Env, A> ma, Aff<A> alternative) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = ma.RunIO(env);
                    return res.IsSucc
                               ? res
                               : await alternative.RunIO().ConfigureAwait(false);
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> IfFail<Env, A>(this Eff<Env, A> ma, Eff<Env, A> alternative) =>
            EffMaybe<Env, A>(
                env =>
                {
                    var res = ma.RunIO(env);
                    return res.IsSucc
                               ? res
                               : alternative.RunIO(env);
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> IfFail<Env, A>(this Eff<Env, A> ma, Eff<A> alternative) =>
            EffMaybe<Env, A>(
                env =>
                {
                    var res = ma.RunIO(env);
                    return res.IsSucc
                               ? res
                               : alternative.RunIO();
                });
        
        //
        // Iter / Do
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, Unit> Iter<Env, A>(this Eff<Env, A> ma, Func<A, Unit> f) =>
            Eff<Env, Unit>(
                env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        f(res.Value);
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, Unit> Iter<Env, A>(this Eff<Env, A> ma, Func<A, Aff<Env, Unit>> f) where Env : struct, HasCancel<Env> =>
            Aff<Env, Unit>(
                async env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        ignore(await f(res.Value).RunIO(env).ConfigureAwait(false));
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, Unit> Iter<Env, A>(this Eff<Env, A> ma, Func<A, Aff<Unit>> f) where Env : struct, HasCancel<Env> =>
            Aff<Env, Unit>(
                async env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        ignore(await f(res.Value).RunIO().ConfigureAwait(false));
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, Unit> Iter<Env, A>(this Eff<Env, A> ma, Func<A, Eff<Env, Unit>> f) =>
            Eff<Env, Unit>(
                env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        ignore(f(res.Value).RunIO(env));
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, Unit> Iter<Env, A>(this Eff<Env, A> ma, Func<A, Eff<Unit>> f) =>
            Eff<Env, Unit>(
                env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        ignore(f(res.Value).RunIO());
                    }
                    return unit;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Do<Env, A>(this Eff<Env, A> ma, Func<A, Unit> f) =>
            EffMaybe<Env, A>(
                env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        f(res.Value);
                    }
                    return res;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Do<Env, A>(this Eff<Env, A> ma, Func<A, Aff<Env, Unit>> f) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        var ures = await f(res.Value).RunIO(env).ConfigureAwait(false);
                        return ures.IsFail
                                   ? Fin<A>.Fail(ures.Error)
                                   : res;
                    }
                    return res;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Do<Env, A>(this Eff<Env, A> ma, Func<A, Eff<Env, Unit>> f) =>
            EffMaybe<Env, A>(
                env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        var ures = f(res.Value).RunIO(env);
                        return ures.IsFail
                                   ? Fin<A>.Fail(ures.Error)
                                   : res;
                    }
                    return res;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Do<Env, A>(this Eff<Env, A> ma, Func<A, Aff<Unit>> f) where Env : struct, HasCancel<Env> =>
            AffMaybe<Env, A>(
                async env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        var ures = await f(res.Value).RunIO().ConfigureAwait(false);
                        return ures.IsFail
                                   ? Fin<A>.Fail(ures.Error)
                                   : res;
                    }
                    return res;
                });
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Do<Env, A>(this Eff<Env, A> ma, Func<A, Eff<Unit>> f) =>
            EffMaybe<Env, A>(
                env =>
                {
                    var res = ma.RunIO(env);
                    if (res.IsSucc)
                    {
                        var ures = f(res.Value).RunIO();
                        return ures.IsFail
                                   ? Fin<A>.Fail(ures.Error)
                                   : res;
                    }
                    return res;
                });
        
        //
        // Filter
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Filter<Env, A>(this Eff<Env, A> ma, Func<A, bool> f) =>
            ma.Bind(x => f(x) ? SuccessEff<A>(x) : FailEff<A>(Error.New(Thunk.CancelledText)));        

        //
        // Bind
        //

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Bind<Env, A, B>(this Eff<Env, A> ma, Func<A, Eff<Env, B>> f) =>
            new Eff<Env, B>(ma.thunk.Map(x => ThunkFromIO(f(x))).Flatten());

        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Bind<Env, A, B>(this Eff<Env, A> ma, Func<A, Eff<B>> f) =>
            new Eff<Env, B>(ma.thunk.Map(x => ThunkFromIO(f(x))).Flatten());

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Bind<Env, A, B>(this Eff<Env, A> ma, Func<A, Aff<Env, B>> f) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.Map(x => ThunkFromIO(f(x))).Flatten());

        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> Bind<Env, A, B>(this Eff<Env, A> ma, Func<A, Aff<B>> f) where Env : struct, HasCancel<Env> =>
            new Aff<Env, B>(ma.thunk.Map(x => ThunkFromIO(f(x))).Flatten());

        //
        // Flatten
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Flatten<Env, A>(this Eff<Env, Eff<Env, A>> ma) =>
            new Eff<Env, A>(ma.thunk.Map(ThunkFromIO).Flatten());
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Flatten<Env, A>(this Eff<Env, Eff<A>> ma) =>
            new Eff<Env, A>(ma.thunk.Map(ThunkFromIO).Flatten());
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Flatten<Env, A>(this Eff<Env, Aff<Env, A>> ma) where Env : struct, HasCancel<Env> =>
            new Aff<Env, A>(ma.thunk.Map(ThunkFromIO).Flatten());
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, A> Flatten<Env, A>(this Eff<Env, Aff<A>> ma) where Env : struct, HasCancel<Env> =>
            new Aff<Env, A>(ma.thunk.Map(ThunkFromIO).Flatten());

        //
        // Select
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> Select<Env, A, B>(this Eff<Env, A> ma, Func<A, B> f) =>
            Map(ma, f);
        
        //
        // SelectMany
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> SelectMany<Env, A, B>(this Eff<Env, A> ma, Func<A, Eff<Env, B>> f) =>
            Bind(ma, f);
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, B> SelectMany<Env, A, B>(this Eff<Env, A> ma, Func<A, Eff<B>> f) =>
            Bind(ma, f);
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> SelectMany<Env, A, B>(this Eff<Env, A> ma, Func<A, Aff<Env, B>> f) where Env : struct, HasCancel<Env> =>
            Bind(ma, f);
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, B> SelectMany<Env, A, B>(this Eff<Env, A> ma, Func<A, Aff<B>> f) where Env : struct, HasCancel<Env> =>
            Bind(ma, f);
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, C> SelectMany<Env, A, B, C>(this Eff<Env, A> ma, Func<A, Eff<Env, B>> bind, Func<A, B, C> project) =>
            Bind(ma, x => Map(bind(x), y => project(x, y)));
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, C> SelectMany<Env, A, B, C>(this Eff<Env, A> ma, Func<A, Eff<B>> bind, Func<A, B, C> project) =>
            Bind(ma, x => Map(bind(x), y => project(x, y)));
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, C> SelectMany<Env, A, B, C>(this Eff<Env, A> ma, Func<A, Aff<Env, B>> bind, Func<A, B, C> project) where Env : struct, HasCancel<Env> =>
            Bind(ma, x => Map(bind(x), y => project(x, y)));
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Aff<Env, C> SelectMany<Env, A, B, C>(this Eff<Env, A> ma, Func<A, Aff<B>> bind, Func<A, B, C> project) where Env : struct, HasCancel<Env> =>
            Bind(ma, x => Map(bind(x), y => project(x, y)));

        //
        // Where
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, A> Where<Env, A>(this Eff<Env, A> ma, Func<A, bool> f) =>
            Filter(ma, f);

        //
        // Zip
        //
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, (A, B)> Zip<Env, A, B>(Eff<Env, A> ma, Eff<Env, B> mb) =>
            new Eff<Env, (A, B)>(Thunk<Env, (A, B)>.Lazy(e =>
            {
                var ta = ma.RunIO(e);
                if (ta.IsFail) return ta.Cast<(A, B)>();
                var tb = mb.RunIO(e);
                if (tb.IsFail) return tb.Cast<(A, B)>();
                return Fin<(A, B)>.Succ((ta.Value, tb.Value));
            }));
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, (A, B)> Zip<Env, A, B>(Eff<A> ma, Eff<Env, B> mb) =>
            new Eff<Env, (A, B)>(Thunk<Env, (A, B)>.Lazy(e =>
            {
                var ta = ma.RunIO();
                if (ta.IsFail) return ta.Cast<(A, B)>();
                var tb = mb.RunIO(e);
                if (tb.IsFail) return tb.Cast<(A, B)>();
                return Fin<(A, B)>.Succ((ta.Value, tb.Value));
            }));
        
        [Pure, MethodImpl(AffOpt.mops)]
        public static Eff<Env, (A, B)> Zip<Env, A, B>(Eff<Env, A> ma, Eff<B> mb) =>
            new Eff<Env, (A, B)>(Thunk<Env, (A, B)>.Lazy(e =>
            {
                var ta = ma.RunIO(e);
                if (ta.IsFail) return ta.Cast<(A, B)>();
                var tb = mb.RunIO();
                if (tb.IsFail) return tb.Cast<(A, B)>();
                return Fin<(A, B)>.Succ((ta.Value, tb.Value));
            }));           
       
        [Pure, MethodImpl(AffOpt.mops)]
        static Thunk<Env, A> ThunkFromIO<Env, A>(Eff<Env, A> ma) =>
            ma.thunk;
    }    
}
