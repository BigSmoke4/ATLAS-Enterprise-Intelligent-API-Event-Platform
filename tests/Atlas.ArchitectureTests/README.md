Executable boundary tests live in `ModuleBoundaryTests.cs` and run in CI.
They load every module assembly and reject domain/application dependencies on
another module's infrastructure, plus direct EF Core dependencies from MVC
controllers. Add a focused rule here whenever a new cross-module contract is
introduced; application interfaces and Shared contracts are the allowed seam.
