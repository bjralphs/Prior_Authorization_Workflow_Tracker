using Prior_Authorization_Workflow_Tracker.Exceptions;
using Prior_Authorization_Workflow_Tracker.Models;

namespace Tests.Unit.Exceptions;

/// <summary>
/// Verifies the custom exception hierarchy (T06, §17.1).
/// Tests are intentionally trivial — they confirm message formatting and inheritance,
/// ensuring catch blocks typed to PaException catch all subtypes.
/// </summary>
public class ExceptionHierarchyTests
{
    [Fact]
    public void WorkflowTransitionException_CarriesStatusValues()
    {
        var ex = new WorkflowTransitionException(PaStatus.Submitted, PaStatus.Approved);

        Assert.Equal(PaStatus.Submitted, ex.FromStatus);
        Assert.Equal(PaStatus.Approved, ex.ToStatus);
        Assert.Contains("Submitted", ex.Message);
        Assert.Contains("Approved", ex.Message);
    }

    [Fact]
    public void WorkflowTransitionException_IsSubclassOfPaException()
    {
        var ex = new WorkflowTransitionException(PaStatus.Draft, PaStatus.Denied);
        Assert.IsAssignableFrom<PaException>(ex);
    }

    [Fact]
    public void BusinessRuleViolationException_ExposesRuleId()
    {
        var ex = new BusinessRuleViolationException("BR-005", "Duplicate active request.");

        Assert.Equal("BR-005", ex.RuleId);
        Assert.Equal("Duplicate active request.", ex.Message);
        Assert.IsAssignableFrom<PaException>(ex);
    }

    [Fact]
    public void AuthorizationException_IsSubclassOfPaException()
    {
        var ex = new AuthorizationException("Access denied.");
        Assert.IsAssignableFrom<PaException>(ex);
        Assert.Equal("Access denied.", ex.Message);
    }

    [Fact]
    public void EntityNotFoundException_FormatsMessage()
    {
        var ex = new EntityNotFoundException("PaRequest", "42");

        Assert.Equal("PaRequest", ex.EntityName);
        Assert.Equal("42", ex.EntityId);
        Assert.Contains("42", ex.Message);
        Assert.IsAssignableFrom<PaException>(ex);
    }

    [Fact]
    public void AllExceptions_CaughtByPaExceptionCatchBlock()
    {
        // Verify all subtypes are caught by a catch(PaException) block.
        static void ThrowAndCatch(PaException ex)
        {
            var caught = Record.Exception(() =>
            {
                Action thrower = () => throw ex;
                thrower();
            });
            Assert.IsAssignableFrom<PaException>(caught);
        }

        ThrowAndCatch(new WorkflowTransitionException(PaStatus.Draft, PaStatus.Expired));
        ThrowAndCatch(new BusinessRuleViolationException("BR-001", "msg"));
        ThrowAndCatch(new AuthorizationException("denied"));
        ThrowAndCatch(new EntityNotFoundException("X", "1"));
    }
}
