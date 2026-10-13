metadata description = 'The monthly budget and its alert. Deployed by hand from the runbook, not by the pipeline: a budget\'s start date cannot change after it is created (A31).'

@description('Builds the budget\'s name, as in main.bicep.')
param namePrefix string = 'idealpoints'

@description('Where the alert is sent.')
@secure()
param alertEmail string

@description('The budget\'s first day, yyyy-MM-dd. The first day of this month for a new budget; the existing date to change one.')
param startDate string

resource budget 'Microsoft.Consumption/budgets@2023-05-01' = {
  name: 'budget-${namePrefix}'
  properties: {
    category: 'Cost'
    amount: 1
    timeGrain: 'Monthly'
    timePeriod: {
      startDate: startDate
    }
    notifications: {
      // thresholds are percentages of the amount: 50 of $1 is fifty cents
      halfTheBudget: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 50
        thresholdType: 'Actual'
        contactEmails: [
          alertEmail
        ]
      }
      forecastToExceed: {
        enabled: true
        operator: 'GreaterThan'
        threshold: 100
        thresholdType: 'Forecasted'
        contactEmails: [
          alertEmail
        ]
      }
    }
  }
}
