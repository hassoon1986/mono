// CS0310: The type 'A' must have a public parameterless constructor in
//         order to use it as parameter 'T' in the generic type or method 'Foo<T>'
// Line: 20

public class Foo<T>
	where T : new ()
{
}

class A
{
	private A ()
	{ }
}

class X
{
	static void Main ()
	{
		object o = new Foo<A> ();
	}
}
