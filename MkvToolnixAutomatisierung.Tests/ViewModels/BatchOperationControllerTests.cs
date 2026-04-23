using MkvToolnixAutomatisierung.ViewModels.Modules;
using Xunit;

namespace MkvToolnixAutomatisierung.Tests.ViewModels;

public sealed class BatchOperationControllerTests
{
    [Fact]
    public void Begin_SetsCancelableState_AndOperationSpecificButtonText()
    {
        using var controller = new BatchOperationController();

        var cancellationToken = controller.Begin(BatchOperationKind.Scan);

        Assert.Equal(BatchOperationKind.Scan, controller.CurrentOperationKind);
        Assert.Equal("Scan abbrechen", controller.CancelButtonText);
        Assert.True(controller.CanCancelCurrentOperation);
        Assert.False(cancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void CancelCurrentOperation_DisablesFurtherCancellation_UntilOperationCompletes()
    {
        using var controller = new BatchOperationController();
        var cancellationToken = controller.Begin(BatchOperationKind.Execution);

        var firstCancel = controller.CancelCurrentOperation();
        var secondCancel = controller.CancelCurrentOperation();

        Assert.True(firstCancel);
        Assert.False(secondCancel);
        Assert.True(cancellationToken.IsCancellationRequested);
        Assert.False(controller.CanCancelCurrentOperation);
        Assert.Equal("Batch abbrechen", controller.CancelButtonText);
    }

    [Fact]
    public void ChangeCurrentOperationKind_KeepsCancellationToken_ForScanComparisonPhase()
    {
        using var controller = new BatchOperationController();
        var cancellationToken = controller.Begin(BatchOperationKind.Scan);

        controller.ChangeCurrentOperationKind(BatchOperationKind.Comparison);
        var cancelled = controller.CancelCurrentOperation();

        Assert.Equal(BatchOperationKind.Comparison, controller.CurrentOperationKind);
        Assert.Equal("Vergleich abbrechen", controller.CancelButtonText);
        Assert.True(cancelled);
        Assert.True(cancellationToken.IsCancellationRequested);
    }

    [Fact]
    public void Complete_ResetsStateAfterOperationFinishes()
    {
        using var controller = new BatchOperationController();
        controller.Begin(BatchOperationKind.Execution);

        controller.Complete(BatchOperationKind.Execution);

        Assert.Equal(BatchOperationKind.None, controller.CurrentOperationKind);
        Assert.False(controller.CanCancelCurrentOperation);
        Assert.Equal("Vorgang abbrechen", controller.CancelButtonText);
    }

    [Fact]
    public void CompleteCurrent_ResetsStateAfterVisibleOperationKindChanged()
    {
        using var controller = new BatchOperationController();
        controller.Begin(BatchOperationKind.Scan);
        controller.ChangeCurrentOperationKind(BatchOperationKind.Comparison);

        controller.CompleteCurrent();

        Assert.Equal(BatchOperationKind.None, controller.CurrentOperationKind);
        Assert.False(controller.CanCancelCurrentOperation);
    }
}
