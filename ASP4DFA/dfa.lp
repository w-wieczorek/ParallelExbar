#include "facts.lp".

{x(P, N) : q(N)} = 1 :- prefix(P).
0 {final(N)} 1 :- q(N).
0 {delta(Q, A, R) : q(R)} 1 :- q(Q), symbol(A).
:- not x(lambda, 0).
:- q(N), x(W, N), negative(W), final(N).
:- q(N), x(W, N), positive(W), not final(N).
x(V, R) :- join(U, A, V), x(U, Q), delta(Q, A, R).
:- join(U, A, V), x(V, N), x(U, J), not delta(J, A, N).

#show final/1.  #show delta/3.
